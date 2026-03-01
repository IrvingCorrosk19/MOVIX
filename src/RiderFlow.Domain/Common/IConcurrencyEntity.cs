namespace RiderFlow.Domain.Common;

public interface IConcurrencyEntity
{
    byte[] RowVersion { get; set; }
}
