using Library;
using Server.Envir;
using System;
using System.Linq;
using System.Reflection;

namespace Server
{
    class Program
    {
        static void Main(string[] args)
        {
            var assembly = Assembly.GetAssembly(typeof(Config));
            ConfigReader.Load(assembly);
            Config.LoadVersion();

            // 单机开发模式：--singleplayer-dev 给测试账号注入满级数据（配合客户端
            // SinglePlayerLauncher 的进程生命周期绑定，双击客户端即可本地测试 UI）。
            if (args.Any(x => string.Equals(x, "--singleplayer-dev", StringComparison.OrdinalIgnoreCase)))
            {
                Config.SinglePlayerDev = true;
                Config.MaxLevel = Math.Max(Config.MaxLevel, DevSinglePlayer.DevLevel);
                Console.WriteLine("[SingleDev] 单机开发模式启用: 测试账号将注入满级数据");
            }
            try
            {
                if (!string.IsNullOrEmpty(Config.EncryptionKey))
                    SEnvir.CryptoKey = Convert.FromBase64String(Config.EncryptionKey);
            }
            catch (Exception)
            {
                throw new ApplicationException($"Invalid format encryption key, expected a base64 with 32 bytes");
            }

            if (Config.EncryptionEnabled && SEnvir.CryptoKey == null)
                throw new ApplicationException($"Encryption is enabled but not specified key [System] => DatabaseKey");

            if (Config.EncryptionEnabled)
                Encryption.SetKey(SEnvir.CryptoKey);

            SEnvir.UseLogConsole = true;
            SEnvir.StartServer();

            Console.CancelKeyPress += Console_CancelKeyPress;

            // We check EnvirThread why when SEnvir is full stoped, set this to null...
            while (SEnvir.EnvirThread != null)
            {
                var command = Console.ReadLine();
                if (command == null)
                {
                    System.Threading.Thread.Sleep(1000);
                    continue;
                }
            }

            ConfigReader.Save(typeof(Config).Assembly);
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            SEnvir.Started = false;
        }
    }
}
