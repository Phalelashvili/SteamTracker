using System;

namespace SteamTracker
{
    public class DataNotReturnedException : Exception
    {
        public DataNotReturnedException()
        {
        }

        public DataNotReturnedException(string message)
            : base(message)
        {
        }

        public DataNotReturnedException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}