param([Parameter(Mandatory)][ValidatePattern('^Ocov2[a-z0-9]+$')][string]$DatabaseSuffix)
$ErrorActionPreference = 'Stop'
$database = 'SecureOps_ResourcesV1_' + $DatabaseSuffix
$sql = New-Object System.Data.SqlClient.SqlConnection("Server=(localdb)\SecureOpsResourcesV1;Database=$database;Integrated Security=true;Encrypt=false")
$sql.Open()
$tx = $sql.BeginTransaction()
try {
    $query = $sql.CreateCommand(); $query.Transaction = $tx
    $query.CommandText = "IF EXISTS(SELECT 1 FROM ops.InUseServerReviews) OR EXISTS(SELECT 1 FROM ops.InUseExecutions) THROW 51220,'Use an untouched isolated fixture.',1; SELECT RecordJson FROM ops.InUseRecords WHERE SourceId='900001';"
    $raw = $query.ExecuteScalar()
    if (!$raw) { throw 'Import the local simulation first.' }
    $template = $raw | ConvertFrom-Json
    if ($template.Draft -or !$template.Source.Synthetic -or $template.Source.IdentityScope -ne 'simulation:local:v1') { throw 'Unexpected baseline fixture.' }
    foreach ($number in @(1,2)) {
        $record = $raw | ConvertFrom-Json
        $query.CommandText = "SELECT Id FROM ops.InUseRecords WHERE SourceId=@source"
        $query.Parameters.Clear(); $null = $query.Parameters.AddWithValue('@source', "90000$number")
        $record.Id = $query.ExecuteScalar().ToString()
        $record.Source.Id = "90000$number"
        $record.Source.Code = "OR-90000$number"
        $record.Source.Title = if ($number -eq 1) { 'EBYS - sentetik DEV sunuculari' } else { 'Ayni sunucu - sonraki sentetik inceleme' }
        $record.Source.Servers = @(foreach ($index in @(1,2)) {
            $id = if ($index -eq 1 -or $number -eq 1) { "120000$index" } else { '1200003' }
            $fields = [ordered]@{}
            $values = [ordered]@{HOSTNAME="review-server-$id";ENVANTER_ID="00012345678901234567890$index";SI_ENVIRONMENT='DEV';
                'SERVER TYPE'='VM';'SERVICE NAME (ÜRÜN/UYGULAMA)'='Elektronik Belge Yonetim Sistemi (EBYS)';
                ITMC_Service_ID='0002915';ITMC_Servis_Unsuru_ID='00112';'SERVICE ASPECT (Servis Unsuru)'='[Genel]';
                'NETWORK SEGMENT'='internal-synthetic';'IP ADDRESS'="192.0.2.$index";'OS NAME'='Windows Server';OS_VERSION='2022';
                CITY='Istanbul';BUILDING='FLORYA BINASI';'RFC Kaydı'='OR-800001'}
            foreach ($entry in $values.GetEnumerator()) { $fields[$entry.Key] = @{Value=$entry.Value;Source='Synthetic bounded browser fixture'} }
            @{Id=$id;Fields=$fields;RelatedRequestReporter=@{ParentId=$record.Source.Id;ServiceItemId=$id;RfcReference='OR-800001';
                ReferenceKind='OrCode';TargetId='800001';OrCode='OR-800001';Display='Deniz Ornek';UserReference='synthetic.reporter';
                State='ExactMatch';DisplayState='Returned';ReferenceState='Returned';LastVerifiedAt=[DateTimeOffset]::UtcNow.ToString('O')}}
        })
        $record.LastSeenAt = [DateTimeOffset]::UtcNow.ToString('O')
        $record.SourceHash = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes(($record.Source | ConvertTo-Json -Depth 30 -Compress)))).Replace('-','')
        $query.Parameters.Clear()
        $query.CommandText = 'UPDATE ops.InUseRecords SET Code=@code,Title=@title,RecordJson=@json WHERE Id=@id AND Version=1;'
        $null=$query.Parameters.AddWithValue('@code',$record.Source.Code); $null=$query.Parameters.AddWithValue('@title',$record.Source.Title)
        $null=$query.Parameters.AddWithValue('@json',($record|ConvertTo-Json -Depth 40 -Compress)); $null=$query.Parameters.AddWithValue('@id',[Guid]$record.Id)
        if ($query.ExecuteNonQuery() -ne 1) { throw 'Fixture changed; refuse overwrite.' }
    }
    $query.Parameters.Clear()
    $query.CommandText = @'
IF NOT EXISTS(SELECT 1 FROM security.Users WHERE CorporateIdentity='demo:platform-admin') THROW 51220,'Missing synthetic actor.',1;
IF NOT EXISTS(SELECT 1 FROM security.Roles WHERE RoleId=32000)
INSERT INTO security.Roles(RoleId,RoleCode,IsSeeded,DisplayName,Purpose,CapabilitiesJson)
VALUES(32000,'SyntheticInUseExecutor',0,N'Sentetik tamamlama',N'Yalniz izole test','["InUse.View","InUse.Review","InUse.Complete"]');
INSERT INTO security.RoleAssignments(UserId,RoleId,GrantedByCorporateIdentity)
SELECT UserId,32000,'synthetic-isolated-browser' FROM security.Users u WHERE CorporateIdentity='demo:platform-admin'
AND NOT EXISTS(SELECT 1 FROM security.RoleAssignments a WHERE a.UserId=u.UserId AND a.RoleId=32000 AND a.RevokedAt IS NULL);
UPDATE security.Users SET DisplayName=N'Sentetik Operator',LoginName='local.operator',Mail='operator@example.invalid',AccessVersion=AccessVersion+1 WHERE CorporateIdentity='demo:platform-admin';
'@
    $null = $query.ExecuteNonQuery()
    $tx.Commit()
    Write-Output "Synthetic two-OR fixture ready in $database. No source calls or corporate grants."
} catch { $tx.Rollback(); throw } finally { $sql.Dispose() }
