﻿#region

using Rnwood.SmtpServer.Verbs;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

#endregion

namespace Rnwood.SmtpServer
{
    public class Server : IServer
    {
        private TcpListener _listener;

        public Server(IServerBehaviour behaviour)
        {
            Behaviour = behaviour;
        }

        public IServerBehaviour Behaviour { get; private set; }

        private bool _isRunning;

        /// <summary>
        /// Gets or sets a value indicating whether the server is currently running.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                return _isRunning;
            }
            private set
            {
                _isRunning = value;
                IsRunningChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler IsRunningChanged;

        public int PortNumber
        {
            get
            {
                return ((IPEndPoint)_listener.LocalEndpoint).Port;
            }
        }

        private IVerbMap GetVerbMap()
        {
            VerbMap verbMap = new VerbMap();
            verbMap.SetVerbProcessor("HELO", new HeloVerb());
            verbMap.SetVerbProcessor("EHLO", new EhloVerb());
            verbMap.SetVerbProcessor("QUIT", new QuitVerb());
            verbMap.SetVerbProcessor("MAIL", new MailVerb());
            verbMap.SetVerbProcessor("RCPT", new RcptVerb());
            verbMap.SetVerbProcessor("DATA", new DataVerb());
            verbMap.SetVerbProcessor("RSET", new RsetVerb());
            verbMap.SetVerbProcessor("NOOP", new NoopVerb());

            return verbMap;
        }

        private void Core()
        {
            try
            {
                while (IsRunning)
                {
                    Task<TcpClient> acceptTask = _listener.AcceptTcpClientAsync();
                    acceptTask.Wait();

                    TcpClient tcpClient = acceptTask.Result;

                    if (IsRunning)
                    {
                        Connection connection = new Connection(this, new TcpClientConnectionChannel(tcpClient), GetVerbMap());
                        _activeConnections.Add(connection);

                        connection.StartProcessing().ContinueWith((connectionTask) =>
                            {
                                _activeConnections.Remove(connection);
                            }
                        );
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                //normal - caused by _listener.Stop();
            }
            catch (AggregateException e)
            {
                if (e.InnerException is ObjectDisposedException)
                {
                    //normal - caused by _listener.Stop() returned by Wait()
                }
                else
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// Runs the server asynchronously. This method returns once the server has been started.
        /// To stop the server call the <see cref="Stop()"/> method.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">if the server is already running.</exception>
        public void Start()
        {
            if (IsRunning)
                throw new InvalidOperationException("Already running");

            _listener = new TcpListener(Behaviour.IpAddress, Behaviour.PortNumber);
            _listener.Start();

            IsRunning = true;

            _coreTaskCancellationToken = new CancellationTokenSource();
            _coreTask = Task.Run(() => Core());
        }

        /// <summary>
        /// Stops the running server. Any existing connections are terminated.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">if the server is not running.</exception>
        public void Stop()
        {
            Stop(true);
        }

        /// <summary>
        /// Stops the running server.
        /// This method blocks until all connections have terminated, either by normal completion or timeout,
        /// or if <paramref name="killConnections"/> has been specified then once all of the threads
        /// have been killed.
        /// </summary>
        /// <param name="killConnections">True if existing connections should be terminated.</param>
        /// <exception cref="System.InvalidOperationException">if the server is not running.</exception>
        public void Stop(bool killConnections)
        {
            if (!IsRunning)
            {
                return;
            }

            _listener.Stop();
            _coreTaskCancellationToken.Cancel();
            _coreTask.Wait();

            if (killConnections)
            {
                foreach (Connection connection in _activeConnections.Cast<Connection>().ToArray())
                {
                    connection.CloseConnection();
                    connection.Terminate();
                }
            }

            IsRunning = false;
        }

        private readonly IList _activeConnections = ArrayList.Synchronized(new List<Connection>());
        private Task _coreTask;
        private CancellationTokenSource _coreTaskCancellationToken;

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Stop();
                }

                disposedValue = true;
            }
        }

        // ~Server() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }

        #endregion
    }
}