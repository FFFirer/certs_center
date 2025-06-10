using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Linq.Expressions;

namespace CertsServer;

public class LocalDateTimeValueConverter : ValueConverter<DateTime, DateTime>
{
    static readonly Expression<Func<DateTime, DateTime>> ConvertToUTC =
       (DateTime time) => time.Kind == DateTimeKind.Utc || time.Kind == DateTimeKind.Unspecified ?
                           time :
                           TimeZoneInfo.ConvertTimeToUtc(time);

    static readonly Expression<Func<DateTime, DateTime>> ConvertFromUTC =
        (DateTime time) => time.Kind != DateTimeKind.Utc || time.Kind == DateTimeKind.Unspecified ?
                            time :
                            time.ToLocalTime();

    public LocalDateTimeValueConverter() : base(ConvertToUTC, ConvertFromUTC)
    {

    }
}

public class LocalDateTimeOffsetValueConverter : ValueConverter<DateTimeOffset, DateTimeOffset> 
{
    static readonly TimeSpan ZeroOffset = TimeSpan.Zero;

    static readonly Expression<Func<DateTimeOffset, DateTimeOffset>> ConvertToUTC =
        (DateTimeOffset time) => time.Offset == ZeroOffset ? time : time.ToOffset(ZeroOffset);

    static readonly Expression<Func<DateTimeOffset, DateTimeOffset>> ConvertFromUTC =
        (DateTimeOffset time) => time.Offset != ZeroOffset ? time : time.ToLocalTime();

    public LocalDateTimeOffsetValueConverter() : base(ConvertToUTC, ConvertFromUTC)
    {

    }
}
