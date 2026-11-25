using System;

namespace Callsign.Core.Ports
{
    /// <summary>Logging port. Implemented in the game layer (file log).</summary>
    public interface ILog
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message, Exception? exception = null);
    }
}
