using System;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class NetworkClient : MonoBehaviour
{
    // ============================================================
    // SERVER CONFIG
    // ============================================================

    private const string SERVER_IP = "127.0.0.1";
    private const int SERVER_PORT = 3636;

    // ============================================================
    // PACKET CONFIG
    // ============================================================

    private const int ENTITY_COUNT = 8;

    // EntityData:
    // float posX = 4 bytes
    // float posY = 4 bytes
    //
    // Total = 8 bytes
    private const int ENTITY_SIZE = 8;

    // Packet:
    // 8 Entity × 8 bytes
    //
    // Total = 64 bytes
    private const int PACKET_SIZE =
        ENTITY_COUNT * ENTITY_SIZE;


    // ============================================================
    // NETWORK
    // ============================================================

    private TcpClient _client;

    private NetworkStream _stream;

    private Thread _receiveThread;

    private volatile bool _running;


    // ============================================================
    // UNITY
    // ============================================================

    private void Start()
    {
        Connect();
    }


    // ============================================================
    // CONNECT
    // ============================================================

    private void Connect()
    {
        try
        {
            Debug.Log(
                $"Connecting to {SERVER_IP}:{SERVER_PORT}..."
            );

            _client = new TcpClient();

            _client.Connect(
                SERVER_IP,
                SERVER_PORT
            );

            _stream = _client.GetStream();

            Debug.Log("Connected to server!");


            // Start network thread

            _running = true;

            _receiveThread = new Thread(
                ReceiveLoop
            );

            _receiveThread.IsBackground = true;

            _receiveThread.Start();
        }
        catch (Exception e)
        {
            Debug.LogError(
                $"Connection failed: {e.Message}"
            );
        }
    }


    // ============================================================
    // RECEIVE LOOP
    // ============================================================

    private void ReceiveLoop()
    {
        byte[] packetBuffer =
            new byte[PACKET_SIZE];


        while (_running)
        {
            try
            {
                // ------------------------------------------------
                // TCP DOES NOT GUARANTEE ONE READ = ONE PACKET
                //
                // Therefore we must receive exactly 64 bytes.
                // ------------------------------------------------

                bool success =
                    ReceiveExactly(
                        packetBuffer,
                        PACKET_SIZE
                    );


                if (!success)
                {
                    Debug.Log(
                        "Server disconnected."
                    );

                    break;
                }


                // ------------------------------------------------
                // Convert binary data → Packet
                // ------------------------------------------------

                Packet packet =
                    DeserializePacket(
                        packetBuffer
                    );


                // ------------------------------------------------
                // SHOW DATA
                // ------------------------------------------------

                PrintPacket(packet);
            }
            catch (Exception e)
            {
                if (_running)
                {
                    Debug.LogError(
                        $"Receive error: {e.Message}"
                    );
                }

                break;
            }
        }
    }


    // ============================================================
    // RECEIVE EXACTLY N BYTES
    // ============================================================

    private bool ReceiveExactly(
        byte[] buffer,
        int size)
    {
        int received = 0;


        while (received < size)
        {
            int bytesRead =
                _stream.Read(
                    buffer,
                    received,
                    size - received
                );


            // Server disconnected

            if (bytesRead == 0)
            {
                return false;
            }


            received += bytesRead;
        }


        return true;
    }


    // ============================================================
    // DESERIALIZE
    // ============================================================

    private Packet DeserializePacket(
        byte[] data)
    {
        Packet packet =
            new Packet();


        for (int i = 0; i < ENTITY_COUNT; i++)
        {
            // Each EntityData = 8 bytes

            int offset =
                i * ENTITY_SIZE;


            // ----------------------------------------------------
            // posX
            // ----------------------------------------------------

            packet.entities[i].posX =
                BitConverter.ToSingle(
                    data,
                    offset
                );


            // ----------------------------------------------------
            // posY
            // ----------------------------------------------------

            packet.entities[i].posY =
                BitConverter.ToSingle(
                    data,
                    offset + 4
                );
        }


        return packet;
    }


    // ============================================================
    // PRINT PACKET
    // ============================================================

    private void PrintPacket(
        Packet packet)
    {
        Debug.Log(
            "================ PACKET ================"
        );


        for (int i = 0; i < ENTITY_COUNT; i++)
        {
            EntityData entity =
                packet.entities[i];


            Debug.Log(
                $"Entity[{i}] " +
                $"Pos(" +
                $"{entity.posX}, " +
                $"{entity.posY}" +
                ")"
            );
        }


        Debug.Log(
            $"Packet Size = {PACKET_SIZE} bytes"
        );


        Debug.Log(
            "========================================"
        );
    }


    // ============================================================
    // CLEANUP
    // ============================================================

    private void OnDestroy()
    {
        _running = false;


        try
        {
            if (_stream != null)
            {
                _stream.Close();
                _stream = null;
            }


            if (_client != null)
            {
                _client.Close();
                _client = null;
            }
        }
        catch
        {
        }


        if (_receiveThread != null &&
            _receiveThread.IsAlive)
        {
            _receiveThread.Join(100);
        }
    }
}


// ================================================================
// ENTITY DATA
// ================================================================

[Serializable]
public struct EntityData
{
    public float posX;

    public float posY;
}


// ================================================================
// PACKET
// ================================================================

[Serializable]
public class Packet
{
    public EntityData[] entities =
        new EntityData[8];
}