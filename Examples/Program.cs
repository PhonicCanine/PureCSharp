using System;
using System.Diagnostics;
using Pure;
using static Pure.PureCSharp;

namespace Examples
{
    class Program
    {
        static ulong fibonacci(ulong n) => n <= 2 ? 1 : fibonacci(n - 1) + fibonacci(n - 2); 

        static void Main(string[] args)
        {
            
            //demonstration of the currying design pattern
            PureFunc<int, Func<int, int>> add = PureFunction<int, Func<int, int>>(
                (add) => (x) => (y) =>
                x + y
            );

            int additionResult = add(300)(43);
            int additionResult2 = add.FI(300, 43);

            //Demonstration of a curried function, which would usually take a long time
            PureFunc<ulong, Func<ulong, ulong>> weirdFib = PureFunction<ulong, Func<ulong, ulong>>(
                (self) => (x) => (n) =>
                n <= 2 ?
                    x : (
                    n <= 40 ?
                        (self(x)(n - 1) + 0) + (self(x)(n - 2) + 0) :
                        (self(x)(n - 1) + self(x)(n - 2))
                )
            );

            ulong weirdFibOutput = weirdFib.FI(1UL, 50UL);

            //Tests of the speed of each type of function
            Stopwatch s = new Stopwatch();
            ulong fibTo = 50;

            s.Start();
            ulong result = fibonacci(fibTo);
            s.Stop();
            Console.WriteLine($"{result} was reached in {s.ElapsedMilliseconds} ms, with standard recursive function.");

            Func<ulong, ulong> fib = RecursiveLambda<ulong, ulong>((self) => (n) => n <= 2 ? 1 : self(n - 1) + self(n - 2));
            s.Restart();
            result = fib(fibTo);
            s.Stop();
            Console.WriteLine($"{result} was reached in {s.ElapsedMilliseconds} ms, with RecursiveLambda.");

            PureFunc<ulong, ulong> fib2 = PureFunction<ulong, ulong>(
                (self) => (n) =>
                n <= 2 ?
                    1 : (
                    n <= 40 ?
                        (self(n - 1) + 0) + (self(n - 2) + 0) :
                        (self(n - 1) + self(n - 2))
                )

            , false,true);

            ResetCache();
            s.Restart();
            result = fib2(fibTo);
            s.Stop();
            Console.WriteLine($"{result} was reached in {s.ElapsedMilliseconds} ms, with PureFunction with only multithreading.");

            fib2 = PureFunction<ulong, ulong>(
                (self) => (n) => 
                n <= 2 ? 
                    1 :
                    (self(n - 1) + self(n - 2))

            , true, false);

            ResetCache();
            s.Restart();
            result = fib2(fibTo);
            s.Stop();
            Console.WriteLine($"{result} was reached in {s.ElapsedMilliseconds} ms, with PureFunction with only caching.");

            fib2 = PureFunction<ulong, ulong>(
                (self) => (n) =>
                n <= 2 ?
                    1 : (
                    n <= 40 ?
                        (self(n - 1) + 0) + (self(n - 2) + 0) :
                        (self(n - 1) + self(n - 2))
                )
            );
            ResetCache();
            s.Restart();
            result = fib2(fibTo);
            s.Stop();
            Console.WriteLine($"{result} was reached in {s.ElapsedMilliseconds} ms, with PureFunction with multithreading & caching.");

            Console.ReadLine();
        }
    }
}
