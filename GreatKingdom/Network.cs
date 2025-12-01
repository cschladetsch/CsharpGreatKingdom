using System;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace GreatKingdom;

public class NetworkManager
{
    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private BinaryReader? _reader;
    private BinaryWriter? _writer;
    
    public ConcurrentQueue<int> IncomingMoves = new ConcurrentQueue<int>();
    
    public bool IsConnected => _client != null && _client.Connected;
    public bool IsHost { get; private set; }

    public async Task<bool> HostGame(int port = 7777)
    {
        try {
            IsHost = true;
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _client = await _listener.AcceptTcpClientAsync();
            SetupStreams();
            return true;
        } catch { return false; }
    }

    public async Task<bool> JoinGame(string ip, int port = 7777)
    {
        try {
            IsHost = false;
            _client = new TcpClient();
            await _client.ConnectAsync(ip, port);
            SetupStreams();
            return true;
        } catch { return false; }
    }

    private void SetupStreams()
    {
        if (_client == null) return;
        _stream = _client.GetStream();
        _reader = new BinaryReader(_stream);
        _writer = new BinaryWriter(_stream);
        Task.Run(ReceiveLoop);
    }

    public void SendMove(int boardIndex)
    {
        if (!IsConnected || _writer == null) return;
        try { _writer.Write(boardIndex); _writer.Flush(); } catch { }
    }

    private void ReceiveLoop()
    {
        try {
            while (IsConnected && _reader != null) {
                int moveIndex = _reader.ReadInt32();
                IncomingMoves.Enqueue(moveIndex);
            }
        } catch { }
    }
}
