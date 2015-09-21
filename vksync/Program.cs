using System;
using System.Linq;
using Clee;
using vksync.Core;

namespace vksync
{
    public class Program
    {
        public static Config config { get; set; } = new Config();

        public static void Main(string[] args)
        {
            var eng = CleeEngine.CreateDefault();
            try
            {
                eng.Execute(args);
            }
            catch (AggregateException ae)
            {
                var messages = ae.Flatten().InnerExceptions.Select(ie => ie.Message);
                Console.WriteLine(string.Join(Environment.NewLine, messages));
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.InnerException?.Message);
            }
        }
    }
}
