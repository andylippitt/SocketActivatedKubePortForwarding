using k8s;
using k8s.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PortForwarding
{
    internal class PortForward
    {
        private class PortForwardConfig
        {
            public string Context { get; set; }
            public string Namespace { get; set; }
            public string PodPattern { get; set; }
            public int PodPort { get; set; }
            public int LocalPort { get; set; }
        }

        private static async Task Main()
        {
            var contextClients = new Dictionary<string, IKubernetes>();

            var configuredPairs = JsonConvert.DeserializeObject<List<PortForwardConfig>>(File.ReadAllText("config.json"));
            var pairs = new List<Task>();

            foreach (var pair in configuredPairs)
            {
                if (!contextClients.ContainsKey(pair.Context))
                    contextClients.Add(pair.Context, new Kubernetes(KubernetesClientConfiguration.BuildConfigFromConfigFile(currentContext: pair.Context)));

                IKubernetes client = contextClients[pair.Context];
                pairs.Add(CreateListenerPair(client, pair.Namespace, pair.PodPattern, pair.PodPort, new IPEndPoint(IPAddress.Any, pair.LocalPort), cancellationToken: default));
            }

            await Task.WhenAny(pairs);
        }

        private static Task CreateListenerPair(IKubernetes client, string ns, string pattern, int port, IPEndPoint endpoint, CancellationToken cancellationToken)
        {
            Console.WriteLine($"{endpoint} : {ns}/{pattern}:{port} ");
            return ListenForConnectionsAsync(
                endpoint,
                async (tcpClient, cancel) =>
                {
                    var list = client.ListNamespacedPod(ns);
                    var pod = list.Items.FirstOrDefault(p => Regex.IsMatch(p.Name(), pattern));

                    Console.WriteLine($"{endpoint} : {ns}/{pod.Name()}:{port}");

                    using var webSocket = await client
                        .WebSocketNamespacedPodPortForwardAsync(pod.Metadata.Name, pod.Namespace(), new int[] { port }, "v4.channel.k8s.io");

                    using var demux = new StreamDemuxer(webSocket, StreamType.PortForward);
                    demux.Start();

                    using var serverStream = demux.GetStream((byte?)0, (byte?)0);
                    using var clientStream = tcpClient.GetStream();

                    // for some reason this isn't working fully async.
                    // with the server demuxed stream, it seems if an async read is in 
                    // progress, then async write is blocked
                    var readServer = new Task(() => serverStream.CopyTo(clientStream));
                    var readClient = new Task(() => clientStream.CopyTo(serverStream));
                    readServer.Start();
                    readClient.Start();

                    // var readServer = serverStream.CopyToAsync(clientStream, cancel);
                    // var readClient = clientStream.CopyToAsync(serverStream, cancel);


                    await Task.WhenAny(
                        readServer,
                        readClient);

                    //Console.WriteLine("Connection closed");
                }, cancellationToken);
        }

        private static async Task ListenForConnectionsAsync(IPEndPoint endpoint, Func<TcpClient, CancellationToken, Task> clientHandler, CancellationToken cancellationToken)
        {
            var listener = new TcpListener(endpoint);
            listener.Start();
            cancellationToken.Register(listener.Stop);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = await listener.AcceptTcpClientAsync();
                    var clientTask = clientHandler.Invoke(tcpClient, cancellationToken)
                        .ContinueWith((antecedent) => tcpClient.Dispose(), TaskScheduler.Default);
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine("TcpListener stopped listening because cancellation was requested.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error handling client: {ex.Message}");
                }
            }
        }
    }
}
