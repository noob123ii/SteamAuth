using System;

namespace SyntaX
{
    internal class InvalidSteamIDException : Exception
    {
        public InvalidSteamIDException() : base("The given Steam ID is not valid.")
        {
        }
    }
}