-- Synthetic predecessor states only, on the fresh harness-owned database before 032.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
INSERT INTO security.Users (UserId, CorporateIdentity, AuthenticationSource, AccessStatus)
VALUES
('00000000-0032-0000-0000-000000000001', N'g34:migration:pending', N'synthetic', N'Pending'),
('00000000-0032-0000-0000-000000000002', N'g34:migration:approved', N'synthetic', N'Approved'),
('00000000-0032-0000-0000-000000000003', N'g34:migration:rejected', N'synthetic', N'Pending');
INSERT INTO security.AccessRequests (AccessRequestId, UserId, Status)
VALUES
('00000000-0032-0000-0001-000000000001', '00000000-0032-0000-0000-000000000001', N'Pending'),
('00000000-0032-0000-0001-000000000002', '00000000-0032-0000-0000-000000000002', N'Approved'),
('00000000-0032-0000-0001-000000000003', '00000000-0032-0000-0000-000000000003', N'Rejected');
INSERT INTO security.AccessRequestHistory (AccessRequestId, Status)
SELECT AccessRequestId, Status FROM security.AccessRequests;
COMMIT TRANSACTION;
