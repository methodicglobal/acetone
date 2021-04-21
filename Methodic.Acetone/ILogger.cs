using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Methodic.Acetone
{

	public enum LogEntryType
	{
		Debug,
		Informational,
		Warning,
		Error
	}

	public interface ILogger
	{
		void WriteEntry(string message, LogEntryType type, int code = 0422);

		bool Enabled { get; set; }
	}

	public class EventLogger : ILogger
	{

		public EventLogger(string appName, bool enabled = true)
		{
			this.AppName = appName;
			this.Enabled = enabled;
		}

		public string AppName { get; }
		public bool Enabled { get; set; }

		public void WriteEntry(string message, LogEntryType type, int code = 0422)
		{
			if (this.Enabled && type != LogEntryType.Debug)
			{
				EventLog.WriteEntry(this.AppName ?? "Acetone", message, MapTypes(type) , code);
			}
			
		}

		private static EventLogEntryType MapTypes(LogEntryType source)
		{
			switch (source)
			{
				case LogEntryType.Error:
					return EventLogEntryType.Error;
				case LogEntryType.Informational:
					return EventLogEntryType.Information;
				case LogEntryType.Warning:
					return EventLogEntryType.Warning;
				default:
					return EventLogEntryType.Information;
			}
		}
	}

	public class TraceLogger : ILogger
	{
		public bool Enabled { get; set; }

		public void WriteEntry(string message, LogEntryType type, int code = 0422)
		{
			if (this.Enabled)
			{
				switch (type)
				{
					default:
					case LogEntryType.Debug:
						Trace.WriteLine(message);
						break;
					case LogEntryType.Error:
						Trace.TraceError(message);
						break;
					case LogEntryType.Informational:
						Trace.TraceInformation(message);
						break;
					case LogEntryType.Warning:
						Trace.TraceWarning(message);
						break;
				}
			}
		}
	}
}
