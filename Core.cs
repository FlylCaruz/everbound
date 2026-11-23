using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace EverBound {
    public sealed class FileSpec {
        public string path { get; set; }
        public string sha256 { get; set; }
        public long size { get; set; }
        public bool preserve { get; set; }
    }
    public sealed class Release {
        public int schema { get; set; }
        public string version { get; set; }
        public string gameVersion { get; set; }
        public string published { get; set; }
        public string package { get; set; }
        public string packageSha256 { get; set; }
        public long packageSize { get; set; }
        public string[] notes { get; set; }
        public FileSpec[] files { get; set; }
    }
    public sealed class Settings {
        public string GameDirectory { get; set; }
        public string ProfileDirectory { get; set; }
        public string UpdateSource { get; set; }
    }
    public sealed class InstalledState {
        public string version { get; set; }
        public string installedUtc { get; set; }
        public FileSpec[] files { get; set; }
    }
    public sealed class ProgressInfo {
        public string Text;
        public double Fraction;
        public bool CanCancel;
        public ProgressInfo(string text, double fraction, bool canCancel) { Text=text; Fraction=fraction; CanCancel=canCancel; }
    }
    public sealed class ReleaseData {
        public Release Manifest;
        public byte[] Json;
        public byte[] Signature;
        public string Source;
        public bool Embedded;
    }

    public static class Data {
        public static readonly Encoding Utf8 = new UTF8Encoding(false,true);
        public const string DefaultSource = "https://github.com/FlylCaruz/everbound/releases/latest/download/manifest.json";
        public const string Site = "https://github.com/FlylCaruz/everbound";
        public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength=1048576, RecursionLimit=32 }; }
        public static T Parse<T>(string text) { return Serializer().Deserialize<T>(text); }
        public static string Json(object value) { return Serializer().Serialize(value); }
        public static string Hash(Stream input) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant(); }
        public static string HashFile(string path) { using(var file=File.OpenRead(path)) return Hash(file); }
        public static string HashBytes(byte[] bytes) { using(var input=new MemoryStream(bytes,false)) return Hash(input); }
        public static byte[] Resource(string name) {
            using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) {
                if(input==null) throw new InvalidDataException("O pacote do launcher está incompleto.");
                using(var output=new MemoryStream()) { input.CopyTo(output); return output.ToArray(); }
            }
        }
        public static void AtomicText(string path,string text) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp=path+".new-"+Guid.NewGuid().ToString("N");
            try {
                File.WriteAllText(temp,text,Utf8);
                if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
            } finally { if(File.Exists(temp)) File.Delete(temp); }
        }
        public static void AssertNoLinks(string path) {
            var current=Path.GetFullPath(path);
            while(!String.IsNullOrEmpty(current)) {
                if((Directory.Exists(current)||File.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                    throw new IOException("Escolha uma pasta comum, sem redirecionamento de arquivos.");
                current=Path.GetDirectoryName(current);
            }
        }
        public static bool IsWithin(string root,string path) {
            var start=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(start,StringComparison.OrdinalIgnoreCase);
        }
        public static string SafePath(string root,string relative) {
            if(String.IsNullOrWhiteSpace(relative) || relative.Contains(":") || relative.Contains("\\") || relative.StartsWith("/") || relative.EndsWith("/") ||
                relative.Split('/').Any(p=>p=="." || p==".." || p.Length==0 || p.EndsWith(".") || p.EndsWith(" ") || p.IndexOfAny(Path.GetInvalidFileNameChars())>=0))
                throw new InvalidDataException("Caminho inválido no pacote.");
            foreach(var part in relative.Split('/')) {
                var stem=part.Split('.')[0].ToUpperInvariant();
                if(stem=="CON"||stem=="PRN"||stem=="AUX"||stem=="NUL"||System.Text.RegularExpressions.Regex.IsMatch(stem,@"^(COM|LPT)[0-9]$"))
                    throw new InvalidDataException("Nome reservado no pacote.");
            }
            var full=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
            if(!IsWithin(root,full)) throw new InvalidDataException("Arquivo fora da pasta do mod.");
            AssertNoLinks(full);
            return full;
        }
        public static bool Protected(string path) {
            var p=path.Replace('\\','/').ToLowerInvariant();
            return p.StartsWith("progression-data/") || p.StartsWith("erOverlay/chat/".ToLowerInvariant()) || p.StartsWith("backups/") ||
                p.EndsWith(".sl2") || p.EndsWith(".sl2.bak") || p.EndsWith(".co2") || p.EndsWith(".co2.bak") || p.EndsWith("/server.json") || p=="server.json" ||
                p.EndsWith("/client.json") || p=="client.json" || p.Contains("private-key") || p.EndsWith(".pfx") || p.EndsWith(".pem") || p.EndsWith(".key");
        }
        public static string Quote(string arg) { if(arg.Contains("\"") || arg.Contains("\r") || arg.Contains("\n")) throw new ArgumentException("Caminho inválido."); return "\""+arg+"\""; }
    }

    public sealed class ReleaseClient {
        readonly string publicKey;
        public ReleaseClient(string key) { publicKey=key; }
        public ReleaseData Verify(byte[] json,byte[] signature,string source,bool embedded) {
            if(json.Length>1048576 || signature.Length>4096) throw new InvalidDataException("Catálogo de atualização inválido.");
            using(var rsa=new RSACryptoServiceProvider()) {
                rsa.PersistKeyInCsp=false;
                rsa.FromXmlString(publicKey);
                if(!rsa.VerifyData(json,CryptoConfig.MapNameToOID("SHA256"),signature))
                    throw new InvalidDataException("A atualização não corresponde a uma versão publicada pelo EverBound.");
            }
            var release=Data.Parse<Release>(Data.Utf8.GetString(json));
            Validate(release);
            return new ReleaseData { Manifest=release,Json=json,Signature=signature,Source=source,Embedded=embedded };
        }
        public static void Validate(Release r) {
            Version version;
            if(r==null || r.schema!=1 || !Version.TryParse(r.version,out version) || r.gameVersion!="2.7.0.0" || r.files==null || r.files.Length<1 || r.files.Length>5000 ||
                r.packageSize<1 || r.packageSize>512L*1024*1024 || !ValidHash(r.packageSha256) || String.IsNullOrEmpty(r.package))
                throw new InvalidDataException("O catálogo de atualização está incompleto ou é incompatível.");
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long size=0;
            foreach(var f in r.files) {
                if(f==null || !seen.Add(f.path) || f.size<0 || f.size>200L*1024*1024 || !ValidHash(f.sha256) || Data.Protected(f.path))
                    throw new InvalidDataException("Lista de arquivos inválida.");
                Data.SafePath(Path.GetTempPath(),f.path);
                size+=f.size;
            }
            if(size>1024L*1024*1024) throw new InvalidDataException("Pacote maior que o permitido.");
            foreach(var required in Game.Required) if(!seen.Contains(required)) throw new InvalidDataException("O pacote não contém todos os componentes do EverBound.");
            if(r.notes==null) r.notes=new string[0];
        }
        static bool ValidHash(string value) { return value!=null && System.Text.RegularExpressions.Regex.IsMatch(value,"^[0-9a-fA-F]{64}$"); }
        public ReleaseData Included() { return Verify(Data.Resource("release.json"),Convert.FromBase64String(Data.Utf8.GetString(Data.Resource("release.sig")).Trim()),"incluído",true); }
        public async Task<ReleaseData> Check(string source,CancellationToken cancel) {
            if(String.IsNullOrWhiteSpace(source)) source=Data.DefaultSource;
            var json=await ReadSmall(source,1048576,cancel);
            var sig=await ReadSmall(SignatureSource(source),8192,cancel);
            return Verify(json,Convert.FromBase64String(Data.Utf8.GetString(sig).Trim()),source,false);
        }
        static string SignatureSource(string source) {
            Uri uri;
            if(Uri.TryCreate(source,UriKind.Absolute,out uri) && !uri.IsFile) return new Uri(uri,"manifest.sig").AbsoluteUri;
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source)),"manifest.sig");
        }
        static HttpWebRequest Request(string source) {
            Uri uri;
            if(!Uri.TryCreate(source,UriKind.Absolute,out uri)||uri.Scheme!="https"||!String.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidDataException("Use um endereço HTTPS de atualizações ou um catálogo local.");
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            var request=(HttpWebRequest)WebRequest.Create(uri);
            request.UserAgent="EverBoundLauncher/1.0";
            request.Timeout=20000; request.ReadWriteTimeout=30000;
            request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
            return request;
        }
        static bool Local(string source) { Uri uri; return !Uri.TryCreate(source,UriKind.Absolute,out uri)||uri.IsFile; }
        static string LocalPath(string source) { Uri uri; return Uri.TryCreate(source,UriKind.Absolute,out uri)&&uri.IsFile ? uri.LocalPath : source; }
        static async Task<byte[]> ReadSmall(string source,int limit,CancellationToken cancel) {
            if(Local(source)) { var bytes=File.ReadAllBytes(LocalPath(source)); if(bytes.Length>limit)throw new InvalidDataException("Catálogo muito grande."); return bytes; }
            var request=Request(source);
            using(cancel.Register(request.Abort)) {
                using(var response=(HttpWebResponse)await request.GetResponseAsync()) {
                    if(response.ResponseUri.Scheme!="https")throw new InvalidDataException("Redirecionamento inválido.");
                    using(var input=response.GetResponseStream()) using(var output=new MemoryStream()) {
                        var buffer=new byte[16384]; int read;
                        while((read=await input.ReadAsync(buffer,0,buffer.Length,cancel))>0) {
                            if(output.Length+read>limit)throw new InvalidDataException("Catálogo muito grande.");
                            output.Write(buffer,0,read);
                        }
                        return output.ToArray();
                    }
                }
            }
        }
        public async Task Download(ReleaseData data,string target,IProgress<ProgressInfo> progress,CancellationToken cancel) {
            var r=data.Manifest;
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            if(data.Embedded) {
                using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("release.zip"))
                using(var output=File.Create(target)) { if(input==null)throw new InvalidDataException("Pacote incluído ausente."); await Copy(input,output,r.packageSize,progress,cancel); }
            } else if(Local(data.Source)) {
                Uri packageUri;
                var name=Uri.TryCreate(r.package,UriKind.Absolute,out packageUri) && packageUri.Scheme=="https" ? Path.GetFileName(packageUri.AbsolutePath) : r.package;
                var package=Data.SafePath(Path.GetDirectoryName(Path.GetFullPath(LocalPath(data.Source))),name);
                if(!File.Exists(package))throw new FileNotFoundException("Coloque o ZIP do EverBound na mesma pasta do catálogo selecionado.");
                using(var input=File.OpenRead(package)) using(var output=File.Create(target)) await Copy(input,output,r.packageSize,progress,cancel);
            } else {
                var url=new Uri(new Uri(data.Source),r.package).AbsoluteUri;
                var request=Request(url);
                using(cancel.Register(request.Abort)) using(var response=(HttpWebResponse)await request.GetResponseAsync()) {
                    if(response.ResponseUri.Scheme!="https")throw new InvalidDataException("Download redirecionado para um endereço inválido.");
                    if(response.ContentLength>r.packageSize)throw new InvalidDataException("Tamanho do download diferente do esperado.");
                    using(var input=response.GetResponseStream()) using(var output=File.Create(target)) await Copy(input,output,r.packageSize,progress,cancel);
                }
            }
            if(new FileInfo(target).Length!=r.packageSize || !String.Equals(Data.HashFile(target),r.packageSha256,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O download está incompleto ou foi alterado. Tente novamente.");
        }
        static async Task Copy(Stream input,Stream output,long expected,IProgress<ProgressInfo> progress,CancellationToken cancel) {
            var buffer=new byte[65536]; long total=0; int read;
            while((read=await input.ReadAsync(buffer,0,buffer.Length,cancel))>0) {
                total+=read; if(total>expected)throw new InvalidDataException("Download maior que o esperado.");
                await output.WriteAsync(buffer,0,read,cancel);
                if(progress!=null)progress.Report(new ProgressInfo("Obtendo pacote · "+(total/1048576.0).ToString("0.0")+" / "+(expected/1048576.0).ToString("0.0")+" MB",0.5*total/expected,true));
            }
        }
    }

    public sealed class Installer {
        public readonly string Storage;
        public Func<bool> IsGameRunning=Game.Running;
        // Used by the isolated transaction test; no environment-variable or production switch.
        internal Action<int> AfterWrite;
        internal Action<string> BackupSavesTo=BackupSaves;
        public Installer(string storage) { Storage=Path.GetFullPath(storage); }
        public static InstalledState State(string profile) {
            var file=Path.Combine(profile,"everbound-installed.json");
            try { return File.Exists(file) ? Data.Parse<InstalledState>(File.ReadAllText(file,Data.Utf8)) : null; } catch { return null; }
        }
        public static List<string> Verify(string profile,Release release) {
            var bad=new List<string>();
            foreach(var f in release.files) {
                var path=Data.SafePath(profile,f.path);
                if(f.preserve && File.Exists(path))continue;
                if(!File.Exists(path)||new FileInfo(path).Length!=f.size||!String.Equals(Data.HashFile(path),f.sha256,StringComparison.OrdinalIgnoreCase))bad.Add(f.path);
            }
            return bad;
        }
        public void Apply(string zip,ReleaseData release,string profile,string gameDirectory,IProgress<ProgressInfo> progress,CancellationToken cancel) {
            if(IsGameRunning()) throw new InvalidOperationException("Feche o Elden Ring pelo menu antes de instalar ou atualizar.");
            profile=Path.GetFullPath(profile);
            if(!String.IsNullOrEmpty(gameDirectory) && (String.Equals(profile.TrimEnd('\\'),Path.GetFullPath(gameDirectory).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)||Data.IsWithin(gameDirectory,profile)))
                throw new InvalidOperationException("Escolha uma pasta do EverBound separada da pasta do jogo.");
            Data.AssertNoLinks(profile); Data.AssertNoLinks(Storage);
            ReleaseClient.Validate(release.Manifest);
            if(new FileInfo(zip).Length!=release.Manifest.packageSize || Data.HashFile(zip)!=release.Manifest.packageSha256.ToLowerInvariant())throw new InvalidDataException("Pacote inválido.");
            var id=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8);
            var stage=Path.Combine(Storage,"staging",id);
            var backup=Path.Combine(Storage,"backups",id);
            Directory.CreateDirectory(stage);
            var journal=new List<string>();
            var hadFile=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var previous=State(profile);
            if(previous!=null && new Version(previous.version)>new Version(release.Manifest.version))throw new InvalidOperationException("Este pacote é mais antigo que sua instalação.");
            bool committed=false;
            try {
                var expected=release.Manifest.files.ToDictionary(f=>f.path,StringComparer.OrdinalIgnoreCase);
                var found=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using(var archive=ZipFile.OpenRead(zip)) {
                    foreach(var entry in archive.Entries) {
                        cancel.ThrowIfCancellationRequested();
                        if(entry.FullName.EndsWith("/")&&entry.Length==0)continue;
                        FileSpec spec;
                        if(!expected.TryGetValue(entry.FullName,out spec)||!found.Add(entry.FullName)||entry.Length!=spec.size)
                            throw new InvalidDataException("O conteúdo do pacote é diferente do catálogo.");
                        if(((entry.ExternalAttributes>>16)&0xf000)==0xa000)throw new InvalidDataException("Links não são aceitos no pacote.");
                        var output=Data.SafePath(stage,entry.FullName);
                        Directory.CreateDirectory(Path.GetDirectoryName(output));
                        using(var input=entry.Open())using(var file=File.Create(output)) {
                            var buffer=new byte[65536]; int n; long bytes=0;
                            while((n=input.Read(buffer,0,buffer.Length))>0) { bytes+=n; if(bytes>spec.size)throw new InvalidDataException("Arquivo maior que o esperado."); file.Write(buffer,0,n); }
                            if(bytes!=spec.size)throw new InvalidDataException("Arquivo incompleto.");
                        }
                        if(!String.Equals(Data.HashFile(output),spec.sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Arquivo alterado: "+spec.path);
                        if(progress!=null)progress.Report(new ProgressInfo("Verificando pacote · "+found.Count+" / "+expected.Count,0.5+0.2*found.Count/expected.Count,true));
                    }
                }
                if(found.Count!=expected.Count)throw new InvalidDataException("Faltam arquivos no pacote.");
                cancel.ThrowIfCancellationRequested();
                if(IsGameRunning())throw new InvalidOperationException("O jogo foi aberto. Feche-o antes de atualizar.");
                if(progress!=null)progress.Report(new ProgressInfo("Salvando cópia de segurança…",0.72,false));
                Directory.CreateDirectory(backup);
                BackupSavesTo(Path.Combine(backup,"saves"));
                var operations=release.Manifest.files.Where(f=>!f.preserve||!File.Exists(Data.SafePath(profile,f.path))).Select(f=>f.path).ToList();
                var removals=previous==null||previous.files==null ? new List<string>() : previous.files.Where(f=>f!=null&&!f.preserve&&!Data.Protected(f.path)&&!expected.ContainsKey(f.path)).Select(f=>f.path).ToList();
                operations.AddRange(removals);
                operations.Add("everbound-installed.json");
                // Back up every destination before the first modification.
                foreach(var relative in operations.Distinct(StringComparer.OrdinalIgnoreCase)) {
                    var destination=Data.SafePath(profile,relative);
                    if(File.Exists(destination)) {
                        var copy=Data.SafePath(Path.Combine(backup,"profile"),relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(copy)); File.Copy(destination,copy,false); hadFile.Add(relative);
                    }
                }
                File.WriteAllText(Path.Combine(backup,"transaction.json"),Data.Json(new { profile=profile,version=release.Manifest.version,files=operations }),Data.Utf8);
                Directory.CreateDirectory(profile);
                int written=0;
                foreach(var relative in operations.Distinct(StringComparer.OrdinalIgnoreCase)) {
                    if(IsGameRunning())throw new InvalidOperationException("O jogo foi aberto durante a atualização. A versão anterior será restaurada.");
                    var destination=Data.SafePath(profile,relative);
                    journal.Add(relative);
                    if(removals.Contains(relative)) { if(File.Exists(destination))File.Delete(destination); }
                    else if(relative=="everbound-installed.json") {
                        Data.AtomicText(destination,Data.Json(new InstalledState { version=release.Manifest.version,installedUtc=DateTime.UtcNow.ToString("o"),files=release.Manifest.files }));
                    } else {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        var temp=destination+".eb-new-"+id;
                        try { File.Copy(Data.SafePath(stage,relative),temp,false); if(File.Exists(destination))File.Replace(temp,destination,null); else File.Move(temp,destination); }
                        finally { if(File.Exists(temp))File.Delete(temp); }
                    }
                    ++written; if(AfterWrite!=null)AfterWrite(written);
                    if(progress!=null)progress.Report(new ProgressInfo("Instalando EverBound · "+written+" / "+operations.Count,0.75+0.24*written/operations.Count,false));
                }
                committed=true;
                if(progress!=null)progress.Report(new ProgressInfo("EverBound atualizado. Pronto para jogar.",1,false));
            } catch {
                var rollbackErrors=new List<Exception>();
                foreach(var relative in journal.AsEnumerable().Reverse()) {
                    try {
                        var destination=Data.SafePath(profile,relative);
                        if(hadFile.Contains(relative)) { Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(Data.SafePath(Path.Combine(backup,"profile"),relative),destination,true); }
                        else if(File.Exists(destination))File.Delete(destination);
                    } catch(Exception ex) { rollbackErrors.Add(ex); }
                }
                if(rollbackErrors.Count>0)throw new IOException("A atualização foi interrompida. A cópia de segurança está em "+backup+". Feche o jogo antes de reparar.",rollbackErrors[0]);
                throw;
            } finally {
                // Only the directory created for this transaction is cleaned up.
                if(Directory.Exists(stage)&&Data.IsWithin(Path.Combine(Storage,"staging"),stage))Directory.Delete(stage,true);
                if(committed)File.WriteAllText(Path.Combine(backup,"completed.txt"),DateTime.UtcNow.ToString("o"),Data.Utf8);
            }
        }
        static void BackupSaves(string target) {
            var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"EldenRing");
            if(!Directory.Exists(root))return;
            foreach(var directory in Directory.GetDirectories(root)) {
                if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)continue;
                foreach(var file in Directory.GetFiles(directory)) {
                    var name=Path.GetFileName(file).ToLowerInvariant();
                    if(!(name.EndsWith(".sl2")||name.EndsWith(".sl2.bak")||name.EndsWith(".co2")||name.EndsWith(".co2.bak")))continue;
                    if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)continue;
                    var output=Path.Combine(target,Path.GetFileName(directory),Path.GetFileName(file));
                    Directory.CreateDirectory(Path.GetDirectoryName(output));File.Copy(file,output,false);
                }
            }
        }
    }

    public static class Game {
        public static readonly string[] Required = {
            "EverBoundStartup.dll","EverBoundTalisman5.dll","EverBoundEquipmentSlots.dll","EverBoundCharacterPreview.dll","EverBoundUnifiedInventory.dll","EverBoundProgression-effects.dll",
            "EROverlay/EROverlay.dll","EROverlay/overlays/EverBoundChat.dll","EROverlay/overlays/EverBoundLevelUp.dll","EROverlay/overlays/Minimap.dll",
            "mod-inventario/menu/01_000_fe.gfx","mod-inventario/menu/02_010_equiptop.gfx","mod-inventario/menu/02_011_equip.gfx","mod-inventario/menu/02_020_inventory.gfx",
            "engine/modengine2_launcher.exe","engine/modengine2/bin/modengine2.dll","engine/modengine2/bin/lua.dll"
        };
        public static bool Running() { var processes=Process.GetProcessesByName("eldenring"); try{return processes.Any(p=>!p.HasExited);}finally{foreach(var p in processes)p.Dispose();} }
        public static bool Present(string profile) { return !String.IsNullOrEmpty(profile)&&Required.Where(p=>!p.StartsWith("engine/")).All(p=>File.Exists(Data.SafePath(profile,p))); }
        public static string ValidateDirectory(string directory) {
            if(String.IsNullOrWhiteSpace(directory))throw new InvalidOperationException("Selecione a pasta do Elden Ring.");
            var exe=Path.Combine(Path.GetFullPath(directory),"eldenring.exe");
            if(!File.Exists(exe))throw new FileNotFoundException("Não encontrei eldenring.exe nessa pasta.");
            if(FileVersionInfo.GetVersionInfo(exe).FileVersion!="2.7.0.0")throw new InvalidOperationException("Esta versão do EverBound requer Elden Ring 1.17 (2.7.0.0).");
            using(var file=File.OpenRead(exe))using(var reader=new BinaryReader(file)) {
                if(reader.ReadUInt16()!=0x5a4d)throw new InvalidDataException("Executável do jogo inválido.");
                file.Position=0x3c; var offset=reader.ReadInt32(); file.Position=offset;
                if(reader.ReadUInt32()!=0x4550 || reader.ReadUInt16()!=0x8664)throw new InvalidDataException("O jogo precisa ser a versão Windows de 64 bits.");
                reader.ReadUInt16(); if(reader.ReadUInt32()!=0x69e9c9b9)throw new InvalidOperationException("O executável do jogo é diferente da versão compatível com este pacote.");
            }
            return Path.GetDirectoryName(exe);
        }
        public static IEnumerable<string> Find() {
            var bases=new List<string>();
            foreach(var key in new[]{@"HKEY_CURRENT_USER\Software\Valve\Steam",@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam"}) {
                var p=Registry.GetValue(key,key.StartsWith("HKEY_CURRENT")?"SteamPath":"InstallPath",null) as string;
                if(!String.IsNullOrEmpty(p))bases.Add(p);
            }
            bases.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam"));
            foreach(var dir in bases.ToArray()) {
                var vdf=Path.Combine(dir,"steamapps","libraryfolders.vdf");
                if(File.Exists(vdf))foreach(System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf),"\"path\"\\s*\"([^\"]+)\"")) bases.Add(m.Groups[1].Value.Replace("\\\\","\\"));
            }
            var candidates=bases.Distinct(StringComparer.OrdinalIgnoreCase).Select(p=>Path.Combine(p,"steamapps","common","ELDEN RING","Game")).ToList();
            foreach(var root in new[]{@"C:\Games",@"D:\Games",@"D:\SteamLibrary",@"E:\SteamLibrary"}) {
                if(!Directory.Exists(root))continue;
                candidates.Add(Path.Combine(root,"steamapps","common","ELDEN RING","Game"));
                try { foreach(var child in Directory.GetDirectories(root).Take(100)) {
                    candidates.Add(Path.Combine(child,"Game")); candidates.Add(Path.Combine(child,"ELDEN RING","Game"));
                } }catch(UnauthorizedAccessException) { }
            }
            return candidates.Where(p=>File.Exists(Path.Combine(p,"eldenring.exe"))).Distinct(StringComparer.OrdinalIgnoreCase);
        }
        public static string Config(string profile) {
            Func<string,string> toml=p=>Data.Quote(Path.GetFullPath(p).Replace('\\','/'));
            var dlls=new[]{"EverBoundStartup.dll","EverBoundTalisman5.dll","EverBoundEquipmentSlots.dll","EverBoundCharacterPreview.dll","EverBoundUnifiedInventory.dll","EverBoundProgression-effects.dll","EROverlay/EROverlay.dll"};
            return "[modengine]\r\ndebug = false\r\nexternal_dlls = ["+String.Join(", ",dlls.Select(p=>toml(Data.SafePath(profile,p))))+"]\r\n\r\n[extension.mod_loader]\r\nenabled = true\r\nloose_params = false\r\nmods = [{ enabled = true, name = \"EverBound\", path = "+toml(Path.Combine(profile,"mod-inventario"))+" }]\r\n\r\n[extension.scylla_hide]\r\nenabled = false\r\n";
        }
        public static async Task Launch(Settings settings) {
            if(Running())throw new InvalidOperationException("O Elden Ring já está aberto.");
            var directory=ValidateDirectory(settings.GameDirectory);
            var profile=Path.GetFullPath(settings.ProfileDirectory);
            if(!Present(profile))throw new InvalidOperationException("Instale ou repare o EverBound antes de jogar.");
            var engine=Path.Combine(profile,"engine","modengine2_launcher.exe");
            // Existing host profiles can keep using the already installed Mod Engine until the first managed update.
            if(!File.Exists(engine))engine=Path.Combine(directory,"modengine2_launcher.exe");
            if(!File.Exists(engine))throw new InvalidOperationException("Falta o inicializador. Use Instalar / reparar.");
            var state=Installer.State(profile);
            if(state!=null && state.files!=null) {
                foreach(var spec in state.files.Where(f=>f.path.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)||f.path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)))
                    if(!File.Exists(Data.SafePath(profile,spec.path))||!String.Equals(Data.HashFile(Data.SafePath(profile,spec.path)),spec.sha256,StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Um componente está ausente ou alterado. Use Verificar arquivos antes de jogar.");
            }
            StartHostChat(profile);
            var config=Path.Combine(profile,"everbound-launcher.toml");
            Data.AtomicText(config,Config(profile));
            var info=new ProcessStartInfo(engine,"-t er -p "+Data.Quote(Path.Combine(directory,"eldenring.exe"))+" -c "+Data.Quote(config));
            info.WorkingDirectory=Path.GetDirectoryName(engine); info.UseShellExecute=false; info.CreateNoWindow=true;
            info.RedirectStandardError=true; info.RedirectStandardOutput=true;
            using(var process=Process.Start(info)) {
                var output=process.StandardOutput.ReadToEndAsync(); var error=process.StandardError.ReadToEndAsync();
                for(int i=0;i<40;++i) {
                    if(Running())return;
                    if(process.HasExited&&process.ExitCode!=0)throw new InvalidOperationException("Não foi possível abrir o jogo. Código "+process.ExitCode+". Verifique a pasta do jogo e repare os arquivos.");
                    await Task.Delay(500);
                }
                if(!Running())throw new InvalidOperationException("O jogo demorou para abrir. Verifique se o Steam está em execução e tente novamente.");
                GC.KeepAlive(output); GC.KeepAlive(error);
            }
        }
        static void StartHostChat(string profile) {
            var server=Path.Combine(profile,"chat","EverBoundChatServer.exe");var config=Path.Combine(profile,"chat","server.json");
            if(!File.Exists(server)||!File.Exists(config))return;
            var running=Process.GetProcessesByName("EverBoundChatServer");
            try { foreach(var p in running) { try { if(String.Equals(p.MainModule.FileName,server,StringComparison.OrdinalIgnoreCase))return; }catch { } } }finally{foreach(var p in running)p.Dispose();}
            var start=new ProcessStartInfo(server,Data.Quote(config));start.WorkingDirectory=Path.GetDirectoryName(server);start.UseShellExecute=false;start.CreateNoWindow=true;
            using(var p=Process.Start(start)) { }
        }
        public static void ImportInvite(string file,string profile) {
            if(Running())throw new InvalidOperationException("Feche o jogo antes de trocar o convite do chat.");
            if(new FileInfo(file).Length>16384)throw new InvalidDataException("Convite inválido.");
            var value=Data.Parse<Dictionary<string,object>>(File.ReadAllText(file,Data.Utf8));
            object host,key,port,name;
            if(!value.TryGetValue("host",out host)||!value.TryGetValue("key",out key)||!value.TryGetValue("port",out port)||!value.TryGetValue("name",out name)||
                !(host is string)||((string)host).Length<1||((string)host).Length>253||!(key is string)||!System.Text.RegularExpressions.Regex.IsMatch((string)key,"^[0-9a-fA-F]{64}$")||
                Convert.ToInt32(port)<1||Convert.ToInt32(port)>65535||!(name is string)||((string)name).Length>64)
                throw new InvalidDataException("Este arquivo não é um convite válido do chat EverBound.");
            var destination=Path.Combine(profile,"EROverlay","chat","client.json");
            Data.AssertNoLinks(destination);
            if(File.Exists(destination))File.Copy(destination,destination+".backup-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss"),false);
            Data.AtomicText(destination,Data.Json(value));
        }
    }
}
