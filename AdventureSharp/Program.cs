using System;
using System.Collections.Generic;

namespace AdventureSharp
{
    public class ConsoleDriver : Driver
    {
        public override void Prompt()
        {
            Console.Write("> ");
        }

        public override string ReadLine()
        {
            return Console.ReadLine();
        }

        public override void WriteLine(string s)
        {
            Console.WriteLine(s);
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var driver = new ConsoleDriver();

            var adventure = new Adventure(driver);
            adventure.Load("test.xml");

            while (adventure.Execute()) ;

            Console.WriteLine("Finished!");
            Console.ReadLine();
        }
    }
}
