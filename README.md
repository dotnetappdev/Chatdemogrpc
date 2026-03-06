# ⚡ gRPC Chat Demo

A .NET 10 peer-to-peer WPF chat application demonstrating **gRPC**, **UDP auto-discovery**, **EF Core + SQLite/SQL Server**, and a **.NET 10 REST API** — all working together cleanly.

---

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│  PC A                              PC B                       │
│  ┌──────────────────┐              ┌──────────────────┐       │
│  │  ChatApp.Client  │◄─── gRPC ───►│  ChatApp.Client  │       │
│  │  (WPF + gRPC     │              │  (WPF + gRPC     │       │
│  │   server on      │              │   server on      │       │
│  │   random port)   │              │   random port)   │       │
│  └────────┬─────────┘              └────────┬─────────┘       │
│           │  UDP broadcast (port 45678)      │                 │
│           └──────────── LAN ─────────────────┘                │
└──────────────────────────────────────────────────────────────┘
                          │ optional
                ┌─────────▼──────────┐
                │  ChatApp.WebApi    │
                │  REST API :5000    │
                │  SQLite (default)  │
                │  or SQL Server     │
                └────────────────────┘
```

### How peer discovery works

1. Each client starts an embedded **gRPC HTTP/2 server** on a random free port.
2. The client broadcasts a **UDP packet** (`port 45678`) to `255.255.255.255` containing its username, display name, and gRPC port.
3. Every other running client on the same subnet receives the packet, does a **gRPC Ping** to confirm reachability, and adds the peer to the contacts list — automatically.
4. A **heartbeat** is sent every 15 seconds; peers that miss 3 heartbeats are removed.
5. A **"bye"** packet is broadcast on graceful shutdown.

> No server, no configuration — just launch on two PCs on the same network.

### Optional REST API

The REST API (`ChatApp.WebApi`) adds:
- Centralised user directory (who's online, their gRPC endpoint)
- Shared message history across devices (stored in SQLite or SQL Server)

Leave the **API URL** field blank in the login screen to run in pure P2P mode. All messages are always saved locally in SQLite regardless.

---

## Projects

| Project | Description |
|---|---|
| `ChatApp.Shared` | gRPC proto file (`chat.proto`), shared DTOs |
| `ChatApp.Data` | EF Core data layer — `User`, `Message` entities, repositories |
| `ChatApp.WebApi` | .NET 10 REST API — user registry + message history |
| `ChatApp.Client` | WPF chat client — gRPC P2P server + UDP discovery + clean UI |

---

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
