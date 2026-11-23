using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Security.Cryptography;
using EverBound;

class VerifyLauncher {
    static int checks;
    static void Assert(bool value,string name){if(!value)throw new Exception("FAIL: "+name);++checks;}
    static void Reject(Action action,string name){bool rejected=false;try{action();}catch{rejected=true;}Assert(rejected,name);}
    static string Snapshot(string root){return String.Join("\n",Directory.GetFiles(root,"*",SearchOption.AllDirectories).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).Select(p=>p.Substring(root.Length)+":"+Data.HashFile(p)));}
    static void Main(string[] args) {
        var root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
        var output=Path.GetFullPath(args[1]);
        var client=new ReleaseClient(File.ReadAllText(args[2]));
        var manifest=Path.Combine(output,"manifest.json");
        var data=client.Verify(File.ReadAllBytes(manifest),Convert.FromBase64String(File.ReadAllText(Path.Combine(output,"manifest.sig"))),manifest,false);
        var package=Path.Combine(output,"EverBound-Client-"+data.Manifest.version+".zip");
        Assert(data.Manifest.files.Length>=28,"complete release");
        var bad=File.ReadAllBytes(manifest);bad[20]^=1;
        Reject(()=>client.Verify(bad,data.Signature,manifest,false),"tampered manifest rejected");
        var badSig=(byte[])data.Signature.Clone();badSig[0]^=1;
        Reject(()=>client.Verify(data.Json,badSig,manifest,false),"tampered signature rejected");
        foreach(var path in new[]{"../outside.dll","/absolute.dll","a\\b.dll","C:/escape.dll","a/../b","a//b","file:stream","CON.txt","LPT1.dll","file. ","a./file"})
            Reject(()=>Data.SafePath(root,path),"unsafe path "+path);
        using(var zip=ZipFile.OpenRead(package))foreach(var entry in zip.Entries)Assert(!Data.Protected(entry.FullName),"no private file "+entry.FullName);
        var download=Path.Combine(root,"local-download.zip");
        client.Download(data,download,null,CancellationToken.None).GetAwaiter().GetResult();
        Assert(Data.HashFile(download)==data.Manifest.packageSha256,"sidecar GitHub release works offline");
        var profile=Path.Combine(root,"Perfil com espaços e acentuação");Directory.CreateDirectory(profile);
        var xp=Path.Combine(profile,"progression-data","private-character.dat");Directory.CreateDirectory(Path.GetDirectoryName(xp));File.WriteAllText(xp,"private XP");
        var credential=Path.Combine(profile,"EROverlay","chat","client.json");Directory.CreateDirectory(Path.GetDirectoryName(credential));File.WriteAllText(credential,"private invitation");
        var preference=Path.Combine(profile,"EROverlay","configs","minimap.ini");Directory.CreateDirectory(Path.GetDirectoryName(preference));File.WriteAllText(preference,"custom preference");
        var installer=new Installer(Path.Combine(root,"storage"));installer.IsGameRunning=()=>false;installer.BackupSavesTo=p=>{Directory.CreateDirectory(p);File.WriteAllText(Path.Combine(p,"fixture.txt"),"fixture save");};
        installer.Apply(download,data,profile,Path.Combine(root,"Game"),null,CancellationToken.None);
        Assert(Installer.Verify(profile,data.Manifest).Count==0,"fresh installation verified");
        Assert(Game.Present(profile),"complete installed profile");
        Assert(File.ReadAllText(xp)=="private XP"&&File.ReadAllText(credential)=="private invitation"&&File.ReadAllText(preference)=="custom preference","XP credentials and preferences preserved");
        var victim=Data.SafePath(profile,"EverBoundStartup.dll");
        File.WriteAllText(victim,"a previous native component");
        Assert(Installer.Verify(profile,data.Manifest).Contains("EverBoundStartup.dll"),"damaged native component detected");
        installer.Apply(download,data,profile,Path.Combine(root,"Game"),null,CancellationToken.None);
        Assert(Installer.Verify(profile,data.Manifest).Count==0,"damaged native component repaired");
        File.WriteAllText(victim,"the previous version must be restored after replacement");
        var before=Snapshot(profile);
        installer.AfterWrite=n=>{if(n==3)throw new IOException("simulated interruption");};
        Reject(()=>installer.Apply(download,data,profile,Path.Combine(root,"Game"),null,CancellationToken.None),"interrupted update");
        Assert(before==Snapshot(profile),"full rollback including installed manifest");
        installer.AfterWrite=null;installer.IsGameRunning=()=>true;
        Reject(()=>installer.Apply(download,data,profile,Path.Combine(root,"Game"),null,CancellationToken.None),"running game blocks update");
        Assert(before==Snapshot(profile),"running game leaves profile untouched");
        installer.IsGameRunning=()=>false;
        var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Reject(()=>installer.Apply(download,data,profile,Path.Combine(root,"Game"),null,cancelled.Token),"cancelled installation");
        Assert(before==Snapshot(profile),"cancel leaves profile untouched");
        File.AppendAllText(download,"tampered");
        Reject(()=>installer.Apply(download,data,profile,Path.Combine(root,"Game"),null,CancellationToken.None),"altered package rejected");
        Assert(before==Snapshot(profile),"altered package leaves profile untouched");
        var config=Game.Config(profile);
        Assert(config.Contains("external_dlls")&&config.Contains("EverBoundStartup.dll")&&config.Contains("EROverlay/EROverlay.dll")&&config.Contains("debug = false"),"launcher loads complete native profile");
        Assert(!config.Contains("pwsh")&&!config.Contains("powershell"),"no PowerShell dependency to start game");
        Reject(()=>Game.ValidateDirectory(Path.Combine(root,"missing-game")),"missing game rejected");
        Console.WriteLine("PASS: "+checks+" checks. Signed releases, path safety, offline download, installation, repair, full rollback, cancellation, running-game protection, private data preservation and native launch config.");
    }
}
