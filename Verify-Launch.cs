using System;
using EverBound;
class VerifyLaunch {
    static int Main(string[] args) {
        try {
            if(args.Length!=2)throw new ArgumentException("Use: Verify-Launch GAME PROFILE");
            var settings=new Settings { GameDirectory=args[0],ProfileDirectory=args[1] };
            Game.Launch(settings).GetAwaiter().GetResult();
            Console.WriteLine("PASS: the native launcher started Elden Ring with the EverBound profile.");
            return 0;
        } catch(Exception ex) {Console.Error.WriteLine(ex.Message);return 1;}
    }
}
