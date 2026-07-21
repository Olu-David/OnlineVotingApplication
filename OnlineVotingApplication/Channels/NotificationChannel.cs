using OnlineVotingApplication.Jobs;
using System.Threading.Channels;

namespace OnlineVotingApplication.Channels
{
    public class NotificationChannel
    {
        
        private readonly Channel<NotificationJob> _channel = Channel.CreateBounded<NotificationJob>(new BoundedChannelOptions(5000)
        {
            FullMode = BoundedChannelFullMode.Wait
        });


          public ChannelWriter<NotificationJob> Writer => _channel.Writer;
    public ChannelReader<NotificationJob> Reader => _channel.Reader;
    }
}
