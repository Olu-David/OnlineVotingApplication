using OnlineVotingApplication.Jobs;
using System.Threading.Channels;

namespace OnlineVotingApplication.Channels
{
    public class DeleteChannel
    {
        private readonly Channel<IDeleteJobs> _channel = Channel.CreateBounded<IDeleteJobs>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        public ChannelWriter<IDeleteJobs> writer => _channel.Writer;
        public ChannelReader<IDeleteJobs> reader => _channel.Reader;    

    }
}
