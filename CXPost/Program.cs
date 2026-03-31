using Microsoft.Extensions.DependencyInjection;

namespace CXPost;

public class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var width = Console.WindowWidth;
            var height = Console.WindowHeight;
            if (width <= 0 || height <= 0)
            {
                Console.Error.WriteLine("CXPost requires an interactive terminal.");
                return 1;
            }
        }
        catch
        {
            Console.Error.WriteLine("CXPost requires an interactive terminal.");
            return 1;
        }

        // DI container will be configured here
        Console.WriteLine("CXPost - TUI Mail Client");
        return 0;
    }
}
