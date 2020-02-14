# Pure CS
*Recursive Lambdas and optimized Pure Functions in C#*

```cs
using static Pure.PureCSharp;
```

*Please note:*
Neither RecursiveLambda or PureFunction will work with lambdas with a block body, due to restrictions of Expressions.

## Features

### Recursive Lambdas

Lambda expressions with a reference to themselves passed in automatically:

For example:
```cs
Func<int, int> fib = RecursiveLambda<int, int>(
    (fibonacci) => (n) => 
    n <= 2 ? //if (n <= 2)
        1 : //return 1
        fibonacci(n - 1) + fibonacci(n - 2) //else, return fib(n - 1) + fib(n - 2)
);
Console.WriteLine(fib(10)); //55
```

### Pure Functions

Lambda Expressions with a few extras:
* Automatic multithreading
* A reference to themselves passed in
* Result caching

Limitations:
* A Pure Function can only call other Pure Functions
* A Pure Function cannot access members of any object, unless the member is a Pure Function

These restrictions are removed if result caching is disabled, and `AllowFunctionsWithoutCachingToBeImpure` is set to `true`.

If caching is enabled, or `AllowFunctionsWithoutCachingToBeImpure` is set to false, and these rules are broken, an `ImpureFunctionException` will be thrown at runtime.

Pure functions are produced by the following two static functions:

```cs
PureFunc<T,TResult> PureFunction<T, TResult>(Expression<Func<Func<T,TResult>, Func<T,TResult>>> ex, bool caching = true, bool threaded = true) //A function, which takes a parameter

PureFunc<T> PureFunction<T>(Expression<Func<T>> expression) //A constant
```

So, for the fibonacci example:

```cs
PureFunc<int, int> fib2 = PureFunction<int, int>(
(fibonacci) => (n) => 
n <= 2 ? 
    1 : 
    fibonacci(n - 1) + fibonacci(n - 2)
);
Console.WriteLine(fib2(10)); //55
```

This should already run faster for larger numbers.

It's worth noting however, that compiling the function before first use (returning from `PureFunction`) can take a while depending on which optimizations are applied.

Similarly, it's worth noting that multithreading is expensive, due to having to synchronize threads. As a result, to best take advantage of the benefits of this feature, when it is used needs to be optimized.

For the fibonacci example, multithreading tends to be faster when `n` is `> 40`. In order to build this into the function, we do the following:

```cs
fib2 = PureFunction<ulong, ulong>(
    (fibonacci) => (n) => 
    n <= 2 ? 
        1 : (
        n <= 40 ? 
            (0 + fibonacci(n - 1)) + (0 + fibonacci(n - 2)) : //by wraping our two function calls in brackets, and performing a useless operation, we prevent this expression from being threaded
            (fibonacci(n - 1) + fibonacci(n - 2)) //this expression will still be threaded
    )
);
```

To ensure that automatic multithreading takes place, simply enclose two function calls in brackets, separated by a standard binary operator (`+,-,*,/,&,&&,|,||,==,<,>,<=,>=,!=`):

For example, this will be threaded:
`(a(args) + b(args))`

And this will not:
`(1 + a(args)) + 1 + b(args)`

#### Clearing the cache

Call `ResetCache()` to clear the function cache.

### Recommended Design Pattern

For functions that need to take multiple parameters, a single `ValueTuple` can be used, or the function can be written such that currying is possible.

This would look like this:
```cs
PureFunc<int,Func<int,int>> add = PureFunction<int,Func<int,int>>(
    (add) => (x) => (y) =>
    x + y
);
```

in order to call this function, in normal C#, we'd usually go:

```cs
add(300)(43);
```

however, in this library we can go:

```cs
add.FI(300,43); //Functional Invoke
```

Curried pure functions get all of the benefits of normal pure function (caching, threading, etc).

Unfortunately, this is not available inside of lambdas, and only inbuilt for functions with up to 4 curried arguments.

### Other Features

`List<B> Map<A,B>(IEnumerable<A> enumerable, Func<A,B> f)`
A standard Map function which may be called over any `IEnumerable<A>`.

`static Func<A,C> Compose<A, B, C>(Func<A,B> a, Func<B,C> b)`
A standard Compose function which may be called with any two compatible functions.

## Performance

Caching makes the biggest difference to performance, and requires the least effort to work with.

However, when the function needs to interact with impure functions, properly implemented multithreading (which is designed to only use multiple threads in certain circumstances), will perform better than single-threaded C# functions.

RecursiveLambdas don't have fantastic performance (due to being compiled at runtime, and missing a lot of optimization), but are good enough for doing simpler tasks.

Calculating `fib(50)`
```
12586269025 was reached in 53570 ms, with standard recursive function.
12586269025 was reached in 78183 ms, with RecursiveLambda.
12586269025 was reached in 26755 ms, with PureFunction with only multithreading.
12586269025 was reached in 1 ms, with PureFunction with only caching.
12586269025 was reached in 5 ms, with PureFunction with multithreading & caching.
```

## Disclaimer
It *is* possible to beat the purity tests for functions with caching enabled, but doing so it not recommended, as it will lead to incorrect values being returned.