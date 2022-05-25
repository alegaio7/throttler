using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Throttler
{
    internal class Logger
    {
        public static void Log(string text)
        {
            Console.WriteLine($"{DateTime.Now.ToString("HH:mm:ss.fff")} TID {Thread.CurrentThread.ManagedThreadId} {text}");
        }
    }
}
