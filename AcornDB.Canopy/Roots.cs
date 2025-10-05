
namespace AcornDB
{
    public class Roots
    {
        public DateTime StartTime { get; } = DateTime.UtcNow;
        public int ShakeCount { get; private set; } = 0;

        public void RegisterShake()
        {
            ShakeCount++;
        }
    }
}
