namespace Whoosh.Core;

public sealed record CheckResult(bool Supported,int Scanned,int Matched,int AlreadyChinese,int Unmatched,int PendingFiles,string ManagedState,string Message);
