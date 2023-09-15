﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EasyConnectLib
{
    public class Rs232Lib : IDisposable, IConnectionPort
    {
        public string? Port => _portName;
        public int? Speed => _speed;

        public int ReceiveTimeout { get; set; } = 1000;
        public int SendTimeout { get; set; } = 1000;

        public bool IsConnected => _serialPort?.IsOpen ?? false;

        public event IConnectionPort.ConnectedEventHandler? ConnectedEvent;

        public event IConnectionPort.DisconnectedEventHandler? DisconnectedEvent;

        public event IConnectionPort.DataReceivedEventHandler? DataReceivedEvent;

        public delegate void PinChangedEventHandler(object sender, PinChangedEventArgs e);
        public event PinChangedEventHandler? PinChangedEvent;

        public event IConnectionPort.ErrorEventHandler? ErrorEvent;

        private SerialPort? _serialPort;
        private string? _portName;
        private int _speed;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private readonly ConcurrentQueue<byte[]> _messageQueue = new ConcurrentQueue<byte[]>();
        private readonly List<byte> receiveBuffer = new List<byte>();
        private bool _disposedValue;

        public Rs232Lib() { }

        public Rs232Lib(string port, int speed)
        {
            Connect(port, speed);
        }

        public bool Connect(string port, int speed)
        {
            _portName = port;
            _speed = speed;
            _messageQueue.Clear();
            receiveBuffer.Clear();
            try
            {
                _serialPort = new SerialPort
                {
                    BaudRate = speed,
                    DataBits = 8,
                    Parity = Parity.None,
                    StopBits = StopBits.One,
                    Handshake = Handshake.None,
                    ReadTimeout = ReceiveTimeout,
                    WriteTimeout = SendTimeout
                };

                _serialPort.DataReceived += OnDataReceivedEventHandler;
                _serialPort.ErrorReceived += OnErrorEventHandler;
                _serialPort.PinChanged += OnPinChangedEventHandler;
            }
            catch (Exception ex)
            {
                Disconnect();
                OnErrorEvent(ex.Message);

                return false;
            }

            OnConnectedEvent();

            Task.Run(() =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    if (IsConnected)
                    {
                        SendDataFromQueue();
                    }
                    else
                    {
                        OnDisconnectedEvent();
                    }
                }
            });

            return true;
        }

        public bool Disconnect()
        {
            _cts.Cancel();
            try
            {
                _serialPort?.Close();
                _serialPort?.Dispose();
            }
            catch (Exception ex)
            {
                OnErrorEvent(ex.Message);

                return false;
            }

            return true;
        }

        public static string[] GetPortList()
        {
            return SerialPort.GetPortNames();
        }

        #region Data acquisition
        public bool Send(byte[] data)
        {
            if (!IsConnected)
                return false;

            _messageQueue.Enqueue(data);
            return true;
        }

        private void SendDataFromQueue()
        {
            if (_messageQueue.TryDequeue(out var message))
                SendData(message);
        }

        private void SendData(byte[] data)
        {
            try
            {
                _serialPort?.Write(data.ToArray(), 0, data.Count());
            }
            catch (Exception ex)
            {
                OnErrorEvent(ex.Message);
            }
        }

        public byte[] Read()
        {
            var result = receiveBuffer.ToArray();
            receiveBuffer.Clear();

            return result;
        }
        #endregion

        #region EventHandlers
        private readonly object _lockReceive = new object();
        private void OnDataReceivedEventHandler(object sender, SerialDataReceivedEventArgs e)
        {
            if (_serialPort?.IsOpen ?? false)
            {
                lock (_lockReceive)
                {
                    var l = _serialPort.BytesToRead;
                    if (l > 0)
                    {
                        var n = 0;
                        var data = new byte[l];
                        try
                        {
                            n = _serialPort.Read(data, 0, l);
                        }
                        catch (Exception ex)
                        {
                            OnErrorEvent(ex.Message);

                            return;
                        }

                        if (DataReceivedEvent != null)
                            OnDataReceivedEvent(data[0..(n - 1)]);
                        else
                            receiveBuffer.AddRange(data[0..(n - 1)]);
                    }
                }
            }
            else
            {
                Disconnect();
            }
        }

        private void OnErrorEventHandler(object sender, SerialErrorReceivedEventArgs e)
        {
            OnErrorEvent(e.EventType.ToString());
        }

        private void OnPinChangedEventHandler(object sender, SerialPinChangedEventArgs e)
        {
            OnPinChangedEvent(e.EventType);
        }
        #endregion

        #region Events
        private void OnConnectedEvent()
        {
            ConnectedEvent?.Invoke(this, EventArgs.Empty);
        }

        private void OnDisconnectedEvent()
        {
            Disconnect();
            DisconnectedEvent?.Invoke(this, EventArgs.Empty);
        }

        private void OnDataReceivedEvent(byte[] data)
        {
            DataReceivedEvent?.Invoke(this, new BinaryDataReceivedEventArgs(data));
        }

        private void OnErrorEvent(string message)
        {
            ErrorEvent?.Invoke(this, new ErrorReceivedEventArgs(message));
        }

        private void OnPinChangedEvent(SerialPinChange pin)
        {
            PinChangedEvent?.Invoke(this, new PinChangedEventArgs(pin));
        }

        #endregion

        #region Dispose
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    Disconnect();
                    _serialPort?.Dispose();
                    _messageQueue.Clear();
                }

                // TODO: set large fields to null
                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method

            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
