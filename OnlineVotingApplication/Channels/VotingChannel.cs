using System.Threading.Channels;
using OnlineVotingApplication.Jobs;
using OnlineVotingApplication.Repository.iServices;

namespace OnlineVotingApplication.Services
{
    public class VotingChannel 
    {

        private readonly Channel<VoteJob> _channel = Channel.CreateBounded<VoteJob>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        public ChannelWriter<VoteJob> Writer => _channel.Writer;
        public ChannelReader<VoteJob> Reader => _channel.Reader;
    }
}