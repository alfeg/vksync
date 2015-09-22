namespace vksync.Sync
{
    public class Music
    {
        public string Title { get; set; }
        public string Id { get; set; }
        public string Artist { get; set; }
        public string Url { get; set; }
        public string Duration { get; set; }

        public override bool Equals(object obj)
        {
            var music = obj as Music;
            if (music != null)
            {
                var item = music;

                return item.Title.Equals(Title);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Title.GetHashCode();
        }
    }
}