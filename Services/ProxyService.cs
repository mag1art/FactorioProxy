using Docker.DotNet;
using Docker.DotNet.Models;
using System.Collections.Concurrent;

namespace FactorioProxy.Services
{
    public class ProxyService : IDisposable
    {
        // IP address of the Factorio server (fixed in docker-compose)
        private readonly string factorioServerIp = "172.54.1.2";
        // UDP port of the Factorio server
        private readonly int factorioServerPort = 34197;
        // Base port for dynamic proxy containers
        private readonly int baseProxyPort = 40000;
        private int nextAvailablePort;

        // Public address for client connections, read from the environment variable PUBLIC_ADDRESS
        private readonly string publicAddress;
        // Lifetime of the proxy container, read from the environment variable PROXY_LIFETIME (in minutes)
        private readonly TimeSpan proxyLifetime;

        // Docker client for interacting with the Docker Engine
        private readonly DockerClient dockerClient;
        // Logger for debug output
        private readonly ILogger<ProxyService> _logger;
        // Storage for active proxy containers, keyed by the allocated port
        private readonly ConcurrentDictionary<int, ProxyContainer> activeProxies = new ConcurrentDictionary<int, ProxyContainer>();
        // Timer for periodically cleaning up expired containers
        private readonly Timer cleanupTimer;
        private bool disposed = false;

        public ProxyService(
            ILogger<ProxyService> logger
            )
        {
            nextAvailablePort = baseProxyPort;
            _logger = logger;

            // Read PUBLIC_ADDRESS from the environment variable
            publicAddress = Environment.GetEnvironmentVariable("PUBLIC_ADDRESS") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(publicAddress))
                _logger.LogError("env PUBLIC_ADDRESS is missing.");

            // Read proxy lifetime (in minutes) from the environment variable PROXY_LIFETIME or default to 59 minutes
            var lifetimeStr = Environment.GetEnvironmentVariable("PROXY_LIFETIME");
            if (!int.TryParse(lifetimeStr, out int lifetimeMinutes))
            {
                lifetimeMinutes = 59;
            }
            proxyLifetime = TimeSpan.FromMinutes(lifetimeMinutes);

            try
            {
                // For Linux use Unix socket; for Windows, use "npipe://./pipe/docker_engine"
                dockerClient = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
                _logger.LogInformation("DockerClient successfully created.");

                // Check connection to Docker using ping
                var pingTask = dockerClient.System.PingAsync();
                pingTask.Wait();
                _logger.LogInformation("Ping to Docker daemon succeeded.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error connecting to Docker daemon during initialization.");
            }

            // Start a timer that checks for expired proxies every minute
            cleanupTimer = new Timer(CleanupExpiredProxies, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// Creates a new container using the alpine/socat image. The container listens for UDP traffic
        /// on an allocated port and forwards it to the Factorio server.
        /// Returns a string with the public address and port for client connection.
        /// </summary>
        public async Task<string> CreateProxy()
        {
            int containerPort = GetNextAvailablePort();
            string containerName = $"socat-proxy-{containerPort}";

            _logger.LogInformation("Creating proxy container. Port: {Port}, Name: {Name}", containerPort, containerName);

            // Note: We pass only two arguments because the image's ENTRYPOINT already starts socat.
            var createParams = new CreateContainerParameters
            {
                Image = "alpine/socat",
                Name = containerName,
                Cmd = new List<string>
                {
                    $"UDP-LISTEN:{containerPort},reuseaddr,fork",
                    $"UDP:{factorioServerIp}:{factorioServerPort}"
                },
                ExposedPorts = new Dictionary<string, EmptyStruct>
                {
                    { $"{containerPort}/udp", new EmptyStruct() }
                },
                HostConfig = new HostConfig
                {
                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        {
                            $"{containerPort}/udp", new List<PortBinding>
                            {
                                new PortBinding { HostPort = containerPort.ToString() }
                            }
                        }
                    },
                    NetworkMode = "proxy_network"
                }
            };

            try
            {
                _logger.LogInformation("Container creation parameters: {@CreateParams}", createParams);
                var response = await dockerClient.Containers.CreateContainerAsync(createParams);
                string containerId = response.ID;
                _logger.LogInformation("Container created. ID: {ContainerId}", containerId);

                bool started = await dockerClient.Containers.StartContainerAsync(containerId, new ContainerStartParameters());
                if (!started)
                {
                    _logger.LogError("Failed to start container with ID: {ContainerId}", containerId);
                    throw new Exception("Failed to start container");
                }
                _logger.LogInformation("Container started. ID: {ContainerId}", containerId);

                var proxyContainer = new ProxyContainer
                {
                    ContainerId = containerId,
                    ContainerPort = containerPort,
                    CreatedAt = DateTime.UtcNow
                };
                activeProxies.TryAdd(containerPort, proxyContainer);

                _logger.LogInformation("Proxy successfully created: {PublicAddress}:{Port}", publicAddress, containerPort);
                return $"{publicAddress}:{containerPort}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating container on port {Port}", containerPort);
                return null;
            }
        }

        /// <summary>
        /// Stops and removes the proxy container for the specified port.
        /// </summary>
        public async Task<bool> RemoveProxy(int containerPort)
        {
            if (activeProxies.TryRemove(containerPort, out var proxy))
            {
                try
                {
                    _logger.LogInformation("Stopping container with ID: {ContainerId}", proxy.ContainerId);
                    await dockerClient.Containers.StopContainerAsync(proxy.ContainerId, new ContainerStopParameters());
                    _logger.LogInformation("Removing container with ID: {ContainerId}", proxy.ContainerId);
                    await dockerClient.Containers.RemoveContainerAsync(proxy.ContainerId, new ContainerRemoveParameters { Force = true });
                    _logger.LogInformation("Container {ContainerId} successfully removed.", proxy.ContainerId);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error removing container with ID: {ContainerId}", proxy.ContainerId);
                    return false;
                }
            }
            else
            {
                _logger.LogWarning("Proxy on port {Port} not found.", containerPort);
                return false;
            }
        }

        /// <summary>
        /// Background method to clean up expired proxies.
        /// Invoked by the timer.
        /// </summary>
        private void CleanupExpiredProxies(object state)
        {
            foreach (var kvp in activeProxies)
            {
                if (DateTime.UtcNow - kvp.Value.CreatedAt > proxyLifetime)
                {
                    _logger.LogInformation("Proxy with ID {ContainerId} on port {Port} has expired. Initiating removal.", kvp.Value.ContainerId, kvp.Value.ContainerPort);
                    _ = RemoveProxy(kvp.Key);
                }
            }
        }

        /// <summary>
        /// Simple logic to allocate the next available port.
        /// </summary>
        private int GetNextAvailablePort()
        {
            return Interlocked.Increment(ref nextAvailablePort);
        }

        /// <summary>
        /// Releases resources by stopping and removing all active containers.
        /// </summary>
        public void Dispose()
        {
            if (disposed)
                return;

            _logger.LogInformation("Dispose called in ProxyService. Stopping all active proxies.");

            foreach (var kvp in activeProxies)
            {
                try
                {
                    RemoveProxy(kvp.Key).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error removing proxy on port {Port} during Dispose", kvp.Key);
                }
            }

            activeProxies.Clear();
            cleanupTimer.Dispose();
            dockerClient.Dispose();

            disposed = true;
            GC.SuppressFinalize(this);
        }

        // Class to store metadata about a running proxy container
        public class ProxyContainer
        {
            public string ContainerId { get; set; }
            public int ContainerPort { get; set; }
            public DateTime CreatedAt { get; set; }
        }
    }
}
