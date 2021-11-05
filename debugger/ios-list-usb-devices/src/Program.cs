using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace JetBrains.Rider.Plugins.Unity.iOS.ListUsbDevices
{
    internal static class Program
    {
        private static CancellationToken GetProcessLifetime()
        {
            var cancellationToken = new CancellationTokenSource();
            Task.Run(() =>
            {
                while (true)
                {
                    var line = Console.ReadLine();
                    if (line?.Equals("stop", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        cancellationToken.Cancel();
                        return;
                    }
                }
            });

            return cancellationToken.Token;
        }
        
        private static async Task<int> Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("Usage: ios-list-usb-devices dllFolderPath sleepInMs");
                Console.WriteLine("  Type 'stop' to finish");
                return -1;
            }

            InitialiseWinSock();
            var iosSupportPath = args[0];
            var pollingInterval = TimeSpan.FromMilliseconds(int.Parse(args[1]));
            
            var lifetime = GetProcessLifetime();
            try
            {
                using var api = new ListDevices(iosSupportPath);
                while (!lifetime.IsCancellationRequested)
                {
                    var devices = api.GetDevices();

                    Console.WriteLine($"{devices.Count}");
                    foreach (var device in devices)
                        Console.WriteLine($"{device.productId:X} {device.udid}");

                    await Task.Delay(pollingInterval, lifetime);
                }
            }
            catch (Exception e)
            {
                if (e.IsOperationCanceled())
                    return 0;

                if (e is AggregateException aggregateException)
                {
                    foreach (var eInner in aggregateException.InnerExceptions)
                        Console.WriteLine(eInner);           
                }
                else
                {
                    Console.WriteLine(e);
                }

                return 1;
            }

            return 0;
        }

        private static void InitialiseWinSock()
        {
            // Small hack to force WinSock to initialise on Windows. If we don't do this, the C based socket APIs in the
            // native dll will fail, because no-one has bothered to initialise sockets.
            try
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                socket.Dispose();
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to create socket (force initialising WinSock on Windows)");
                Console.WriteLine(e);
            }
        }
        
        public static bool IsOperationCanceled(this Exception exception)
        {
            switch (exception)
            {
                case null:
                    return false;
                case OperationCanceledException _:
                    return true;
                case AggregateException aggregate when aggregate.InnerExceptions.Count == 0:
                    return false;
                case AggregateException aggregate:
                {
                    // ReSharper disable once LoopCanBeConvertedToQuery
                    foreach(var inner in aggregate.InnerExceptions)
                    {
                        if (!inner.IsOperationCanceled())
                            return false;
                    }

                    //all inner exceptions are OCE
                    return true;
                }
                 
                default:
                    return exception.InnerException.IsOperationCanceled();
            }
        }
    }
}
