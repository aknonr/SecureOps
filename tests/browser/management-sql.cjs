const assert = require('node:assert/strict');
const { execFileSync, spawn } = require('node:child_process');

function databaseArgs(database) {
    assert.match(database, /^SecureOps_ResourcesV1_[A-Za-z0-9_]{1,40}$/);
    return ['-S', '(localdb)\\SecureOpsResourcesV1', '-d', database, '-E', '-I', '-b', '-h', '-1', '-W'];
}

function query(database, sql) {
    return execFileSync('sqlcmd', [...databaseArgs(database), '-Q', 'SET NOCOUNT ON; ' + sql], { encoding: 'utf8' }).trim();
}

function seed(database) {
    const day = new Date();
    day.setUTCHours(0, 0, 0, 0);
    day.setUTCDate(day.getUTCDate() - 2);
    const from = day.toISOString();
    const to = new Date(day.getTime() + 86400000).toISOString();
    query(database, `
        IF EXISTS(SELECT 1 FROM audit.AuditLog WHERE Actor='synthetic:management-boundary')
            THROW 51000, 'Use a fresh journey database.', 1;
        DECLARE @from datetimeoffset(7)='${from}', @to datetimeoffset(7)='${to}';
        INSERT INTO audit.AuditLog(OccurredAt,Actor,Action)
        VALUES(DATEADD(nanosecond,-100,@from),'synthetic:management-boundary','IdentityLookupSucceeded'),
              (@from,'synthetic:management-boundary','IdentityLookupSucceeded'),
              (DATEADD(nanosecond,-100,@to),'synthetic:management-boundary','IdentityLookupNotFound'),
              (@to,'synthetic:management-boundary','IdentityLookupSucceeded');
        DECLARE @id uniqueidentifier=NEWID();
        INSERT INTO ops.OperationalRecords(OperationalRecordId,SourceRecordId,OrCode,Title,Description,
            Classification,JiraEligible,EligibilityReason,WorkflowState,UpdatedAt)
        VALUES(@id,'synthetic-management-history','SYN-REPORT-1','Synthetic reporting history',
            'Historical transitions, current manual review. Not eligible for publication.',
            'NeedsManualReview',0,'No approved policy','NeedsManualReview',@to);
        INSERT INTO ops.OperationalRecordWorkflowHistory(OperationalRecordId,WorkflowState,Actor,CorrelationId,OccurredAt)
        VALUES(@id,'Eligible','synthetic:management-boundary','synthetic-report',@from),
              (@id,'Previewed','synthetic:management-boundary','synthetic-report',DATEADD(hour,1,@from)),
              (@id,'Completed','synthetic:management-boundary','synthetic-report',DATEADD(hour,2,@from)),
              (@id,'NeedsManualReview','synthetic:management-boundary','synthetic-report',@to);
    `);
    return { from, to };
}

async function lockHistory(database) {
    const process = spawn('sqlcmd', databaseArgs(database), { stdio: ['pipe', 'pipe', 'pipe'] });
    const exited = new Promise(resolve => process.once('exit', resolve));
    try {
        await new Promise((resolve, reject) => {
            const timer = setTimeout(() => reject(new Error('SQL lock was not acquired')), 15000);
            const inspect = data => {
                if (data.toString().includes('WASAS_LOCK_READY')) { clearTimeout(timer); resolve(); }
            };
            process.stdout.on('data', inspect);
            process.stderr.on('data', inspect);
            process.once('error', error => { clearTimeout(timer); reject(error); });
            process.once('exit', code => { clearTimeout(timer); reject(new Error('SQL lock exited: ' + code)); });
            process.stdin.write("BEGIN TRANSACTION;\nSELECT COUNT(*) FROM ops.OperationalRecordWorkflowHistory WITH(TABLOCKX,HOLDLOCK);\nRAISERROR('WASAS_LOCK_READY',10,1) WITH NOWAIT;\nGO\n");
        });
    } catch (error) {
        process.stdin.end('ROLLBACK;\nGO\n:EXIT\n');
        await exited;
        throw error;
    }
    return async () => {
        process.stdin.end('ROLLBACK;\nGO\n:EXIT\n');
        assert.equal(await exited, 0, 'SQL lock release');
    };
}

module.exports = { query, seed, lockHistory };
