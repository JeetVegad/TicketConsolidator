using System;

namespace HashVerify
{
    class Program
    {
        static void Main(string[] args)
        {
            string secret = "sys:admin--v";
            int hash = GetDeterministicHashCode(secret);
            Console.WriteLine($"SECRET: '{secret}'");
            Console.WriteLine($"HASH: {hash}");
        }

        private static int GetDeterministicHashCode(string str)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in str)
                {
                    hash = hash * 31 + c;
                }
                return hash;
            }
        }
    }
}
