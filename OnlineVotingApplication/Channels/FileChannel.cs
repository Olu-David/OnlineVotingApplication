using OnlineVotingApplication.Jobs;
using System.Threading.Channels;

namespace OnlineVotingApplication.Channels
{
    public class FileChannel
    {

        private readonly Channel<IFileJob> _channel = Channel.CreateBounded<IFileJob>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        public ChannelWriter<IFileJob> Writer => _channel.Writer;
        public ChannelReader<IFileJob> Reader => _channel.Reader;
    }
}
