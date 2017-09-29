using System.Threading.Tasks;

namespace UnitTests
{
  public class Either<TLeft, TRight>
    where TLeft : class
    where TRight : class
  {

    TLeft Left { get; }
    TRight Right { get; }
    bool IsLeft => Left != null;
    bool IsRight => Right != null;

    private Either(TLeft left, TRight right)
    {
      Left = left;
      Right = right;
    }

    public static Either<TLeft, TRight> FromLeft(TLeft left) {
      return new Either<TLeft, TRight>(left, null);
    }

    public static Either<TLeft, TRight> FromRight(TRight right) {
      return new Either<TLeft, TRight>(null, right);
    }
  }

  public static class TaskExtensions
  {
      public static async Task<Either<L,R>> Or<L,R>(this Task<L> left, Task<R> right)
        where L : class
        where R : class
      {
        var firstCompletedTask = await Task.WhenAny(
          left.ContinueWith(t => Either<L,R>.FromLeft(t.Result), TaskContinuationOptions.OnlyOnRanToCompletion),
          right.ContinueWith(t => Either<L,R>.FromRight(t.Result), TaskContinuationOptions.OnlyOnRanToCompletion)
        );
        return firstCompletedTask.Result;
      }
  }
}
