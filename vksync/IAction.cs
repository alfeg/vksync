namespace vksync
{
    public interface IAction<T>
    {
        T Act(T state);
    }
}