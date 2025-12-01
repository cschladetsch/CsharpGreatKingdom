using System;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace GreatKingdom;

public class NetworkManager
{
    private TcpListener _listener;
    private TcpClient _client;
    private NetworkStream _stream;
    private BinaryReader _reader;
    private BinaryWriter _writer;
    
    // Thread-safe queue to store incoming moves so the Game Loop can pick them up
    public ConcurrentQueue<int> IncomingMoves = new ConcurrentQueue<int>();
    
    public bool IsConnected => _client != null && _client.Connected;
    public bool IsHost { get; private set; }

    // --- CONNECTION LOGIC ---

    public async Task<bool> HostGame(int port = 7777)
    {
        try
        {
            IsHost = true;
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Console.WriteLine($"[Host] Listening on port {port}...");

            // Wait for a player to join
            _client = await _listener.AcceptTcpClientAsync();
            SetupStreams();
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Error] Host failed: {e.Message}");
            return false;
        }
    }

    public async Task<bool> JoinGame(string ip, int port = 7777)
    {
        try
        {
            IsHost = false;
            _client = new TcpClient();
            await _client.ConnectAsync(ip, port);
            SetupStreams();
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Error] Join failed: {e.Message}");
            return false;
        }
    }

    private void SetupStreams()
    {
        _stream = _client.GetStream();
        _reader = new BinaryReader(_stream);
        _writer = new BinaryWriter(_stream);

        // Start listening for messages in the background
        Task.Run(ReceiveLoop);
    }

    // --- DATA TRANSMISSION ---

    public void SendMove(int boardIndex)
    {
        if (!IsConnected) return;
        try
        {
            // Packet Protocol: Just send the integer index
            _writer.Write(boardIndex); 
            _writer.Flush();
        }
        catch (Exception e) { Console.WriteLine($"Send Error: {e.Message}"); }
    }

    private void ReceiveLoop()
    {
        try
        {
            while (IsConnected)
            {
                // BLOCKING CALL: Waits here until data arrives
                int moveIndex = _reader.ReadInt32();
                
                // Push to queue for the Main Thread to handle
                IncomingMoves.Enqueue(moveIndex);
            }
        }
        catch (Exception)
        {
            // Disconnection happens here
            Console.WriteLine("Disconnected.");
        }
    }
}
