﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using NLog.Common;
using NLog.Config;
using RabbitMQ.Client;
using RabbitMQ.Client.Framing;

namespace NLog.Targets
{
	/// <summary>
	/// A RabbitMQ-target for NLog.
	/// </summary>
	[Target("RabbitMQ")]
	public class RabbitMq : TargetWithLayout
	{
		private IConnection _connection;
		private IModel _model;
		private readonly Encoding _encoding = Encoding.UTF8;
		private readonly DateTime _epoch = new DateTime(1970, 1, 1, 0, 0, 0, 0);
		private readonly List<Tuple<byte[], IBasicProperties, string>> _unsentMessages
			= new List<Tuple<byte[], IBasicProperties, string>>(512);

		#region Properties

		private string _VHost = "/";

		/// <summary>
		/// 	Gets or sets the virtual host to publish to.
		/// </summary>
		public string VHost
		{
			get { return _VHost; }
			set { if (value != null) _VHost = value; }
		}

		private string _UserName = "guest";

		/// <summary>
		/// 	Gets or sets the username to use for
		/// 	authentication with the message broker. The default
		/// 	is 'guest'
		/// </summary>
		public string UserName
		{
			get { return _UserName; }
			set { _UserName = value; }
		}

		private string _Password = "guest";

		/// <summary>
		/// 	Gets or sets the password to use for
		/// 	authentication with the message broker.
		/// 	The default is 'guest'
		/// </summary>
		public string Password
		{
			get { return _Password; }
			set { _Password = value; }
		}

		private ushort _Port = 5672;

		/// <summary>
		/// 	Gets or sets the port to use
		/// 	for connections to the message broker (this is the broker's
		/// 	listening port).
		/// 	The default is '5672'.
		/// </summary>
		public ushort Port
		{
			get { return _Port; }
			set { _Port = value; }
		}

		private string _Topic = "{0}";

		///<summary>
		///	Gets or sets the routing key (aka. topic) with which
		///	to send messages. Defaults to {0}, which in the end is 'error' for log.Error("..."), and
		///	so on. An example could be setting this property to 'ApplicationType.MyApp.Web.{0}'.
		///	The default is '{0}'.
		///</summary>
		public string Topic
		{
			get { return _Topic; }
			set { _Topic = value; }
		}

		private IProtocol _Protocol = Protocols.DefaultProtocol;

		/// <summary>
		/// 	Gets or sets the AMQP protocol (version) to use
		/// 	for communications with the RabbitMQ broker. The default 
		/// 	is the RabbitMQ.Client-library's default protocol.
		/// </summary>
		public IProtocol Protocol
		{
			get { return _Protocol; }
			set { if (value != null) _Protocol = value; }
		}

		private string _HostName = "localhost";

		/// <summary>
		/// 	Gets or sets the host name of the broker to log to.
		/// </summary>
		/// <remarks>
		/// 	Default is 'localhost'
		/// </remarks>
		public string HostName
		{
			get { return _HostName; }
			set { if (value != null) _HostName = value; }
		}

		private string _Exchange = "app-logging";

		/// <summary>
		/// 	Gets or sets the exchange to bind the logger output to.
		/// </summary>
		/// <remarks>
		/// 	Default is 'log4net-logging'
		/// </remarks>
		public string Exchange
		{
			get { return _Exchange; }
			set { if (value != null) _Exchange = value; }
		}

		/// <summary>
		/// 	Gets or sets the application id to specify when sending. Defaults to null,
		/// 	and then IBasicProperties.AppId will be the name of the logger instead.
		/// </summary>
		public string AppId { get; set; }

		private int _MaxBuffer = 10240;

		/// <summary>
		/// Gets or sets the maximum number of messages to save in the case
		/// that the RabbitMQ instance goes down. Must be >= 1. Defaults to 10240.
		/// </summary>
		public int MaxBuffer
		{
			get { return _MaxBuffer; }
			set { if (value > 0) _MaxBuffer = value; }
		}

		private ushort _heartBeatSeconds = 3;

		/// <summary>
		/// Gets or sets the number of heartbeat seconds to have for the RabbitMQ connection.
		/// If the heartbeat times out, then the connection is closed (logically) and then
		/// re-opened the next time a log message comes along.
		/// </summary>
		public ushort HeartBeatSeconds
		{
			get { return _heartBeatSeconds; }
			set {  _heartBeatSeconds = value; }
		}

	    public bool UseJSON { get; set; }

	    public bool Durable { get; set; }

        private IList<Field> _fields = new List<Field>();

        [ArrayParameter(typeof(Field), "field")]
        public IList<Field> Fields
        {
            get { return _fields; }
            private set { _fields = value; }
        }

        #endregion

        protected override void Write(AsyncLogEventInfo logEvent)
		{
			var basicProperties = GetBasicProperties(logEvent);
			var message = GetMessage(logEvent);
			var routingKey = string.Format(_Topic, logEvent.LogEvent.Level.Name);

			if (_model == null || !_model.IsOpen)
				StartConnection();

			if (_model == null || !_model.IsOpen)
			{
				AddUnsent(routingKey, basicProperties, message);
				return;
			}

			try
			{
				CheckUnsent();
				Publish(message, basicProperties, routingKey);
			}
			catch (IOException e)
			{
				InternalLogger.Error("Could not send to RabbitMQ instance! {0}", e.ToString());
				AddUnsent(routingKey, basicProperties, message);
				ShutdownAmqp(_connection, new ShutdownEventArgs(ShutdownInitiator.Application, Constants.ChannelError, "Could not talk to RabbitMQ instance"));
			}
		}

		private void AddUnsent(string routingKey, IBasicProperties basicProperties, byte[] message)
		{
			if (_unsentMessages.Count < _MaxBuffer)
				_unsentMessages.Add(Tuple.Create(message, basicProperties, routingKey));
			else
				InternalLogger.Warn("MaxBuffer {0} filled. Ignoring message.", _MaxBuffer);
		}

		private void CheckUnsent()
		{
			var count = _unsentMessages.Count;

			for (var i = 0; i < count; i++)
			{
				var tuple = _unsentMessages[i];
				InternalLogger.Info("publishing unsent message: {0}.", tuple);
				Publish(tuple.Item1, tuple.Item2, tuple.Item3);
			}

			if (count > 0) 
				_unsentMessages.Clear();
		}

		private void Publish(byte[] bytes, IBasicProperties basicProperties, string routingKey)
		{
			_model.BasicPublish( _Exchange,
			                    routingKey,
			                    true, basicProperties,
			                    bytes);
		}

		private byte[] GetMessage(AsyncLogEventInfo logEvent)
		{
		    var msg = MessageFormatter.GetMessageInner(UseJSON, Layout, logEvent.LogEvent, Fields);
            return _encoding.GetBytes(msg);
		}

        private IBasicProperties GetBasicProperties(AsyncLogEventInfo loggingEvent)
		{
			var @event = loggingEvent.LogEvent;
			
			var basicProperties = new BasicProperties();
			basicProperties.ContentEncoding = "utf8";
			basicProperties.ContentType = UseJSON ? "application/json" : "text/plain";
			basicProperties.AppId = AppId ?? @event.LoggerName;

			basicProperties.Timestamp = new AmqpTimestamp(MessageFormatter.GetEpochTimeStamp(@event));

			// support Validated User-ID (see http://www.rabbitmq.com/extensions.html)
			basicProperties.UserId = UserName;

			return basicProperties;
		}

		protected override void InitializeTarget()
		{
			base.InitializeTarget();

			StartConnection();
		}

		/// <summary>
		/// Never throws
		/// </summary>
		[MethodImpl(MethodImplOptions.Synchronized)]
		private void StartConnection()
		{
			try
			{
				_connection = GetConnectionFac().CreateConnection();
				_connection.ConnectionShutdown += ShutdownAmqp;

				try { _model = _connection.CreateModel(); }
				catch (Exception e)
				{
					InternalLogger.Error("could not create model", e);
				}

				_model?.ExchangeDeclare(_Exchange, ExchangeType.Topic, Durable);
			}
			catch (Exception e)
			{
				InternalLogger.Error("could not connect to Rabbit instance", e);
			}
		}

		private ConnectionFactory GetConnectionFac()
		{
			return new ConnectionFactory
			{
				HostName = HostName,
				VirtualHost = VHost,
				UserName = UserName,
				Password = Password,
				RequestedHeartbeat = HeartBeatSeconds,
				Port = Port
			};
		}

		[MethodImpl(MethodImplOptions.Synchronized)]
		private void ShutdownAmqp(object connectionBoxed, ShutdownEventArgs reason)
		{
			// I can't make this NOT hang when RMQ goes down
			// and then a log message is sent...

			//try
			//{
			//    if (_Model != null && _Model.IsOpen)
			//        _Model.Abort(); //_Model.Close();
			//}
			//catch (Exception e)
			//{
			//    InternalLogger.Error("could not close model", e);
			//}
		    var connection = connectionBoxed as IConnection;
			try
			{
				if (connection != null && connection.IsOpen)
				{
					connection.ConnectionShutdown -= ShutdownAmqp;
					connection.Close(reason.ReplyCode, reason.ReplyText, 1000);
					connection.Abort(1000); // you get 2 seconds to shut down!
				}
			}
			catch (Exception e)
			{
				InternalLogger.Error("could not close connection", e);
			}
		}

		// Dispose calls CloseTarget!

		protected override void CloseTarget()
		{
			ShutdownAmqp(_connection, new ShutdownEventArgs(ShutdownInitiator.Application, Constants.ReplySuccess, "closing appender"));
			base.CloseTarget();
		}
	}
}