namespace HCore.UI
{
    public interface IUIOpenable
    {
        public void Open();
        public void Close();
        public bool IsOpen { get; }
    }
}
