using Microsoft.EntityFrameworkCore;
using RentRight.Data;

namespace RentRight.Tests.SqlIntegration.Performance;

internal static class SyntheticQueryData
{
    // Only called with a freshly migrated SqlApplication database in a disposable container.
    public static Task SeedAsync(ApplicationDbContext db, int properties) => db.Database.ExecuteSqlInterpolatedAsync($"""
        SET NOCOUNT ON;
        DECLARE @size int = {properties};
        CREATE TABLE #Numbers (n int PRIMARY KEY);
        WITH digits AS (SELECT n FROM (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) d(n))
        INSERT #Numbers SELECT 1+a.n+10*b.n+100*c.n+1000*d.n
        FROM digits a CROSS JOIN digits b CROSS JOIN digits c CROSS JOIN digits d
        WHERE 1+a.n+10*b.n+100*c.n+1000*d.n <= @size;

        INSERT AspNetUsers (Id,UserName,NormalizedUserName,Email,NormalizedEmail,FullName,
            EmailConfirmed,PhoneNumberConfirmed,TwoFactorEnabled,LockoutEnabled,AccessFailedCount)
        SELECT CONCAT('synthetic-',n), CONCAT('user',n,'@example.test'), CONCAT('USER',n,'@EXAMPLE.TEST'),
            CONCAT('group',n%20,'@example.test'), CONCAT('GROUP',n%20,'@EXAMPLE.TEST'), 'Synthetic user',
            1,0,0,0,0 FROM #Numbers;
        INSERT AspNetUserRoles (UserId,RoleId)
        SELECT u.Id,r.Id FROM AspNetUsers u CROSS JOIN AspNetRoles r
        WHERE u.Id LIKE 'synthetic-%' AND
            (r.Name='Tenant' OR (r.Name='Landlord' AND TRY_CAST(SUBSTRING(u.Id,11,10) AS int)%10=0)
            OR (r.Name='Admin' AND TRY_CAST(SUBSTRING(u.Id,11,10) AS int)%100=0));

        INSERT Properties (Title,Address,RentAmount,Description,LandlordId)
        SELECT CONCAT('Home ',n%40),CONCAT('Synthetic street ',n%80),800+n%2000,
            REPLICATE(N'x',200), CASE WHEN n%2=1 THEN 'OwningLandlord' WHEN n%4=0 THEN 'OtherLandlord'
            ELSE CONCAT('synthetic-',10*(1+n%10)) END
        FROM #Numbers ORDER BY n;
        INSERT Tenancies (PropertyId,TenantId,StartDate,EndDate,Status)
        SELECT Id,CASE WHEN Id=1 THEN 'Tenant' ELSE CONCAT('synthetic-',Id) END,
            '2026-01-01',CASE WHEN Id%10=0 THEN '2026-10-16' ELSE NULL END,'Active'
        FROM Properties WHERE Id%10<7 ORDER BY Id;
        INSERT Tenancies (PropertyId,TenantId,StartDate,EndDate,Status)
        SELECT Id,CASE WHEN Id=1 THEN 'Tenant' ELSE CONCAT('synthetic-',Id) END,
            '2024-01-01','2024-12-31','Ended' FROM Properties ORDER BY Id;
        INSERT MaintenanceRequests (TenancyId,Description,DateSubmitted,LastUpdatedUtc,Status,Priority,Revision)
        SELECT t.Id,REPLICATE(N'x',200),DATEADD(day,-(n.n%60),CAST('2026-10-17T12:00:00' AS datetime2)),
            '2026-10-17',CASE WHEN n.n%20=0 THEN 'Submitted' WHEN n.n%20=1 THEN 'InProgress'
            WHEN n.n%20<6 THEN 'Resolved' ELSE 'Closed' END,'Normal',0
        FROM Tenancies t CROSS JOIN #Numbers n
        WHERE n.n<=20 OR (t.PropertyId=1 AND t.Status='Active' AND n.n<=200) ORDER BY t.Id,n.n;
        INSERT MaintenanceStatusEvents (MaintenanceRequestId,Status,OccurredUtc,IsLegacyBaseline)
        SELECT Id,Status,DateSubmitted,1 FROM MaintenanceRequests;
        DROP TABLE #Numbers;
        UPDATE STATISTICS Properties WITH FULLSCAN;
        UPDATE STATISTICS Tenancies WITH FULLSCAN;
        UPDATE STATISTICS MaintenanceRequests WITH FULLSCAN;
        UPDATE STATISTICS AspNetUsers WITH FULLSCAN;
        UPDATE STATISTICS AspNetUserRoles WITH FULLSCAN;
        """);
}
