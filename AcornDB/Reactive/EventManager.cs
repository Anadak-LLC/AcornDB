using System.Reactive.Subjects;

namespace AcornDB.Reactive
{
    public class EventManager<T>
    {
        private readonly Subject<T> _subject = new Subject<T>();

        public void Subscribe(Action<T> callback)
        {
            _subject.Subscribe(callback);
        }

        public void RaiseChanged(T document)
        {
            _subject.OnNext(document);
        }
    }
}
