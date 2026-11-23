using System;
using System.IO;
using System.Threading;
using EverBound;
class VerifyRemote {
    static int Main(string[] args) {
        try {
            var root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
            var client=new ReleaseClient(File.ReadAllText(args[1]));
            var release=client.Check(Data.DefaultSource,CancellationToken.None).GetAwaiter().GetResult();
            var zip=Path.Combine(root,"download.zip");
            client.Download(release,zip,null,CancellationToken.None).GetAwaiter().GetResult();
            var profile=Path.Combine(root,"clean-client");
            var installer=new Installer(Path.Combine(root,"transactions"));
            installer.IsGameRunning=()=>false;
            installer.BackupSavesTo=p=>Directory.CreateDirectory(p);
            installer.Apply(zip,release,profile,Path.Combine(root,"game-fixture"),null,CancellationToken.None);
            var bad=Installer.Verify(profile,release.Manifest);
            if(bad.Count!=0||!Game.Present(profile))throw new Exception("Remote install verification failed.");
            Console.WriteLine("PASS: anonymous GitHub update endpoint; valid signature; verified download; clean installation of "+release.Manifest.files.Length+" files; version "+release.Manifest.version+".");
            return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex.ToString());return 1;}
    }
}
