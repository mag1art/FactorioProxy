# UDP Factorio Proxy

## Introduction

The **UDP Factorio Proxy** is a web API service built with ASP.NET Core (.NET 8) that dynamically creates Docker containers running the `alpine/socat` image to act as UDP proxies for Factorio. This solution enables players to connect to a Factorio server even when on different networks by providing a virtual LAN environment. Each proxy container listens on a dedicated UDP port and forwards traffic to the Factorio server.

## Features

- **Dynamic Proxy Creation:**  
  Creates individual Docker containers for each client using `alpine/socat` to forward UDP traffic.
  
- **Programmatic Docker Management:**  
  Uses the [Docker.DotNet](https://github.com/Microsoft/Docker.DotNet) library to manage Docker containers programmatically, eliminating the need for a docker CLI inside the application container.
  
- **Configurable Environment:**  
  The public address (`PUBLIC_ADDRESS`) and proxy lifetime (`PROXY_LIFETIME`) are configurable via environment variables.
  
- **Automatic Cleanup:**  
  Implements background cleanup of expired proxy containers and supports manual removal via API endpoints.
  
- **Integrated Swagger UI:**  
  Built-in Swagger documentation provides easy API testing and exploration.
  
- **Multi-container Setup:**  
  A Docker Compose configuration sets up the API, Factorio server, and a shared Docker network with static IP assignments.

## Requirements

- Docker installed on the host machine.
- .NET 8 SDK and runtime.
- Docker Compose (version 3.8 or higher).

## Getting Started

### docker-compose.yml

The [`docker-compose.yml`](docker-compose.yml) file defines two services:

- **udp-proxy-api:**  
  The ASP.NET Core Web API service that creates and manages proxy containers.

- **factorio-server:**  
  A Factorio server container (using the `factoriotools/factorio` image) that listens for game traffic on UDP port `34197` and RCON connections on TCP port `27015`.

Both services are connected to a shared network (`proxy_network`) with static IP addresses to ensure that the proxy service can reliably forward traffic to the Factorio server.

### Environment Variables

The following environment variables are used:

- **PUBLIC_ADDRESS**  
  The public address that clients should use to connect to the proxy.

- **PROXY_LIFETIME**  
  The lifetime of a proxy container in minutes (default: `59`).

These variables can be set in your deployment environment or within a `.env` file used by Docker Compose.

## Running the Application

To run the application using Docker Compose, execute:

```bash
docker-compose up --build
```

This command builds the API container and starts both the UDP proxy API and Factorio server containers.

## API Endpoints

### Create Proxy

- **Endpoint:** `POST /api/proxy`
- **Description:** Creates a new proxy container.
- **Response:** Returns a string in the format `PUBLIC_ADDRESS:port` for client connection.

### Remove Proxy

- **Endpoint:** `DELETE /api/proxy/{port}`
- **Description:** Stops and removes the proxy container running on the specified port.

### Swagger UI

Access the Swagger UI for interactive API documentation at:

```
http://<your-host>:80/swagger
```

## Cleanup and Shutdown

The `ProxyService` class implements `IDisposable` to ensure that all active proxy containers are stopped and removed when the application shuts down.

## Contributing

Contributions are welcome! If you have suggestions, bug fixes, or improvements, please open an issue or submit a pull request.

## License

This project is licensed under the MIT License.

---

Happy gaming and proxying!
