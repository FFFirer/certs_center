using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Text.Json;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CertsServer.Data;

public class ObjectValueConverter<T> : ValueConverter<T?, string>
{
    public readonly static ObjectValueConverter<T> Singleton = new ObjectValueConverter<T>();

    public static JsonSerializerOptions Options = new JsonSerializerOptions();

    public ObjectValueConverter() : base(
        o => Serialize(o),
        s => Deserialize(s))
    {

    }

    public static string Serialize(T? @object) => JsonSerializer.Serialize(@object, Options);
    public static T? Deserialize(string value) => JsonSerializer.Deserialize<T>(value, Options);
}

public static class DateTimeExtensions
{
    [return: NotNullIfNotNull("ticks")]
    public static DateTimeOffset? ToDateTime(this long? ticks)
    {
        if (ticks is null) { return null; }
        return new DateTime(ticks.Value, DateTimeKind.Utc).ToLocalTime();
    }

    [return: NotNullIfNotNull("ticks")]
    public static DateTime? ToUtcDateTime(this long? ticks)
    {
        if (ticks is null) return null;

        return new DateTime(ticks.Value, DateTimeKind.Utc).ToLocalTime();
    }

    [return: NotNullIfNotNull("time")]
    public static long? ToTicks(this DateTimeOffset? time)
    {
        return time?.UtcTicks;
    }

    [return: NotNullIfNotNull("time")]
    public static DateTime? ConvertToUtc(this DateTime? time)
    {
        if (time is null) { return null; }

        if (time.Value.Kind == DateTimeKind.Utc || time.Value.Kind == DateTimeKind.Unspecified)
        {
            return time;
        }

        return TimeZoneInfo.ConvertTimeToUtc(time.Value);
    }

    [return: NotNullIfNotNull("time")]
    public static DateTime? ConvertFromUtc(this DateTime? time)
    {
        if (time is null) { return null; }

        if (time.Value.Kind == DateTimeKind.Utc || time.Value.Kind == DateTimeKind.Unspecified)
        {
            return time;
        }

        return time.Value.ToLocalTime();
    }

    static readonly TimeSpan ZeroOffset = TimeSpan.Zero;

    [return: NotNullIfNotNull("time")]
    public static DateTimeOffset? ConvertToUtc(this DateTimeOffset? time)
            => time?.Offset == ZeroOffset ? time : time?.ToOffset(ZeroOffset);

    [return: NotNullIfNotNull("time")]
    public static DateTimeOffset? ConvertFromUtc(DateTimeOffset? time)
        => time?.Offset != ZeroOffset ? time : time?.ToLocalTime();
}

public class SqliteDateTimeOffsetValueConverter : ValueConverter<DateTimeOffset?, long?>
{
    public SqliteDateTimeOffsetValueConverter() : base(
        time => DateTimeExtensions.ToTicks(DateTimeExtensions.ConvertToUtc(time)),
        ticks => DateTimeExtensions.ConvertFromUtc(DateTimeExtensions.ToDateTime(ticks))
    )
    {

    }

    public readonly static ValueConverter<DateTimeOffset?, long?> Instance = new SqliteDateTimeOffsetValueConverter();
}

public class SqliteDateTimeValueConverter : ValueConverter<DateTime?, long?>
{
    public SqliteDateTimeValueConverter() : base(
        time => DateTimeExtensions.ToTicks(DateTimeExtensions.ConvertToUtc(time)),
        ticks => DateTimeExtensions.ConvertFromUtc(DateTimeExtensions.ToUtcDateTime(ticks))
    )
    {

    }

    public readonly static ValueConverter<DateTime?, long?> Instance = new SqliteDateTimeValueConverter();
}


public class DateTimeOffsetToTicksValueConverter : ValueConverter<DateTimeOffset, long>
{
    public DateTimeOffsetToTicksValueConverter() : base(
        v => (long)DateTimeExtensions.ToTicks(v),
        v => (DateTimeOffset)DateTimeExtensions.ToDateTime(v))
    {
    }

    // public static DateTimeOffset ToDateTimeOffset(long ticks)
    // {
    //     return new DateTime(ticks).ToLocalTime();
    // }

    // public static long ToTicks(DateTimeOffset dateTimeOffset)
    // {
    //     return dateTimeOffset.UtcTicks;
    // }

    public static readonly DateTimeOffsetToTicksValueConverter Singleton = new DateTimeOffsetToTicksValueConverter();
}

public class LocalDateTimeValueConverter : ValueConverter<DateTime, DateTime>
{
    static readonly Expression<Func<DateTime, DateTime>> ConvertToUTCExpr =
       (DateTime time) => (DateTime)DateTimeExtensions.ConvertToUtc(time);

    static readonly Expression<Func<DateTime, DateTime>> ConvertFromUTCExpr =
        (DateTime time) => (DateTime)DateTimeExtensions.ConvertFromUtc(time);

    private LocalDateTimeValueConverter() : base(ConvertToUTCExpr, ConvertFromUTCExpr)
    {

    }

    public static readonly ValueConverter<DateTime, DateTime> Singlten = new LocalDateTimeValueConverter();
}

public class LocalDateTimeOffsetValueConverter : ValueConverter<DateTimeOffset, DateTimeOffset>
{

    static readonly Expression<Func<DateTimeOffset, DateTimeOffset>> ConvertToUTCExpr =
        (DateTimeOffset time) => (DateTimeOffset)DateTimeExtensions.ConvertToUtc(time);

    static readonly Expression<Func<DateTimeOffset, DateTimeOffset>> ConvertFromUTCExpr =
        (DateTimeOffset time) => (DateTimeOffset)DateTimeExtensions.ConvertFromUtc(time);

    private LocalDateTimeOffsetValueConverter() : base(ConvertToUTCExpr, ConvertFromUTCExpr)
    {

    }


    public static readonly ValueConverter<DateTimeOffset, DateTimeOffset> Singleton = new LocalDateTimeOffsetValueConverter();
}