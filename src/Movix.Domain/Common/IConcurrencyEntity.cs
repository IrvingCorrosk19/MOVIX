namespace Movix.Domain.Common;

public interface IConcurrencyEntity
{
    byte[] RowVersion { get; set; }
}
