# ⚡ gRPC Chat Demo

A .NET 10 peer-to-peer WPF chat application demonstrating **gRPC**, **UDP auto-discovery**, **EF Core + SQLite/SQL Server**, and a **.NET 10 REST API** — all working together cleanly.

---

## Architecture

```
┌──────────────────────────────────────────────────────────────────────┐
│  PC A                                  PC B                           │
│  ┌──────────────────────┐              ┌──────────────────────┐       │
│  │  ChatApp.Client (WPF)│◄─ gRPC P2P ─►│  ChatApp.Client (WPF)│       │
│  │  (thin UI layer only)│              │  (thin UI layer only)│       │
│  └──────────┬───────────┘              └──────────┬───────────┘       │
│             │   references                         │                   │
│  ┌──────────▼───────────────────────────────────────────────────┐     │
│  │                     ChatApp.Core                              │     │
│  │  UdpDiscoveryService  ·  PeerNetworkService (gossip)         │     │
│  │  GrpcHostService      ·  PeerChatService (GetKnownPeers)     │     │
│  │  HistoryService       ·  KnownPeersStore  ·  ApiService      │     │
│  └───────────────────────────────────────────────────────────────┘    │
│             │  UDP broadcast  (port 45678)                             │
│             └──────────────────────────────────────────────────────── │
└──────────────────────────────────────────────────────────────────────┘
                               │ optional
                    ┌──────────▼───────────┐
                    │   ChatApp.WebApi      │
                    │   REST API :5000      │
                    │   SQLite (default)    │  ◄── ChatApp.Data
                    │   or SQL Server       │
                    └───────────────────────┘
```

### Project structure

| Project | Layer | Description |
|---|---|---|
| `ChatApp.Shared` | Shared | gRPC proto file, DTO models |
| `ChatApp.Data` | Data | EF Core — `User`/`Message` entities, repositories |
| `ChatApp.Core` | **Business logic** | All services + interfaces — no WPF dependency |
| `ChatApp.WebApi` | API | .NET 10 REST API (optional, SQLite or SQL Server) |
| `ChatApp.Client` | UI | WPF thin layer — ViewModels, Views, Converters only |
| `tests/ChatApp.Core.Tests` | Tests | 22 unit tests for Core services |
| `tests/ChatApp.Data.Tests` | Tests | 14 unit tests for repositories |
| `tests/ChatApp.WebApi.Tests` | Tests | 18 unit tests for API controllers |

### How decentralised discovery works

1. Each client starts an embedded **gRPC HTTP/2 server** on a random free port.
2. The client broadcasts a **UDP packet** (`port 45678`) to `255.255.255.255`.
3. On discovery, a **gRPC Ping** confirms reachability.
4. The client calls **`GetKnownPeers`** (new gossip RPC) — the peer returns its known-peers list.
5. The client connects to each of those peers too (one-hop gossip → full mesh).
6. All seen peers are **persisted to local SQLite**; on next launch they're reconnected directly.
7. A **heartbeat** is sent every 15 seconds; stale peers are evicted after 50 seconds.
8. A **"bye"** UDP packet is broadcast on graceful shutdown.

> No server, no configuration — launch on any two PCs on the same LAN and they find each other automatically. For cross-network, add one peer manually and the gossip propagates your full mesh.



## Getting Started

### Prerequisites
- Windows 10/11 (WPF is Windows-only)
- .NET 10 SDK
- (Optional) SQL Server for production-grade shared history

### Run the WPF client (P2P mode — no server needed)

```bash
cd src/ChatApp.Client
dotnet run
```

- Enter a username and click **Connect**.
- Leave the API URL blank.
- Open another instance on the same machine (or a different PC on the same network).
- They will **discover each other automatically** via UDP broadcast.

### Run the REST API (optional)

```bash
cd src/ChatApp.WebApi
dotnet run
```

- Defaults to **SQLite** (`chat.db` in the working directory) — no SQL Server needed.
- Listens on `http://0.0.0.0:5000` — reachable from any PC on the LAN.

To use SQL Server instead, set the connection string in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "SqlServer": "Server=.;Database=ChatDemoDb;Trusted_Connection=True;"
  }
}
```

### Connect the client to the API

In the login screen, enter:

```
http://<api-pc-ip>:5000
```

---

## gRPC Contract (`chat.proto`)

```protobuf
service ChatService {
    rpc SendMessage        (SendMessageRequest)  returns (SendMessageResponse);
    rpc Subscribe          (SubscribeRequest)    returns (stream ChatMessageProto);
    rpc Ping               (PingRequest)         returns (PingResponse);
    rpc SendTypingIndicator(TypingRequest)        returns (TypingResponse);
}
```

Each WPF instance **hosts** this service (acts as a server) _and_ **calls** it on peers (acts as a client) — true P2P.

---

## REST API Endpoints

| Method | Route | Description |
|---|---|---|
| POST | `/api/users/register` | Register / update a user (upsert) |
| GET | `/api/users` | All users |
| GET | `/api/users/online` | Online users only |
| GET | `/api/users/{name}` | Single user |
| PUT | `/api/users/{name}/status` | Update status / endpoint |
| DELETE | `/api/users/{name}` | Unregister |
| POST | `/api/messages` | Save a message |
| GET | `/api/messages/{u1}/{u2}` | Conversation history |
| GET | `/api/messages/unread/{to}/{from}` | Unread count |
| POST | `/api/messages/markread/{to}/{from}` | Mark as read |

---

## Key gRPC Principles Demonstrated

| Principle | Where |
|---|---|
| **Unary RPC** | `SendMessage`, `Ping`, `SendTypingIndicator` |
| **Server-streaming RPC** | `Subscribe` — push messages to connected peers |
| **HTTP/2 multiplexing** | All gRPC calls reuse a single connection per peer |
| **Protobuf serialisation** | Fast binary encoding for all messages |
| **Embedded server** | Each WPF process hosts Kestrel + gRPC — no separate server |
| **Deadline / timeout** | Ping uses a 3-second deadline |
