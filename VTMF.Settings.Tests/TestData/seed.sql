INSERT INTO dbo.processtypes (NAME, DISPLAYLABEL, TAG, PROCESSTYPE, DATAPROCEDURES, SETTINGPROCEDURES, ENVIRONMENT, ORDERNR)
VALUES (N'tproc', N'Test process', N'test', N'TaskDocument', NULL, NULL, N'wterminal', 10),
       (N'tprocvariant', N'Test variant', N'test', N'TaskDocument', N'tproc', N'tproc', N'wterminal', 20),
       (N'tother', N'Other process', N'test', N'RelocateItems', NULL, NULL, N'wterminal', 30);
GO

CREATE TABLE dbo.tprocProcessSettings (NAME nvarchar(200) NOT NULL, VALUE nvarchar(400) NOT NULL, settingtype nvarchar(20) NOT NULL, LISTVALUE nvarchar(1600) NULL, workercode nvarchar(100) NOT NULL, ordernr int NOT NULL);
CREATE TABLE dbo.tprocDocumentListLayout (NAME nvarchar(200) NOT NULL, VALUE nvarchar(400) NOT NULL, settingtype nvarchar(20) NOT NULL, LISTVALUE nvarchar(1600) NULL, workercode nvarchar(100) NOT NULL, ordernr int NOT NULL);
CREATE TABLE dbo.tprocDocumentListLayoutwterminal2 (NAME nvarchar(200) NOT NULL, VALUE nvarchar(400) NOT NULL, settingtype nvarchar(20) NOT NULL, LISTVALUE nvarchar(1600) NULL, workercode nvarchar(100) NOT NULL, ordernr int NOT NULL);
CREATE TABLE dbo.tprocDocumentListLayouten (NAME nvarchar(200) NOT NULL, VALUE nvarchar(400) NOT NULL, settingtype nvarchar(20) NOT NULL, LISTVALUE nvarchar(1600) NULL, workercode nvarchar(100) NOT NULL, ordernr int NOT NULL);
CREATE TABLE dbo.tprocDocumentItemsListLayout (NAME nvarchar(200) NOT NULL, VALUE nvarchar(400) NOT NULL, settingtype nvarchar(20) NOT NULL, LISTVALUE nvarchar(1600) NULL, workercode nvarchar(100) NOT NULL, ordernr int NOT NULL);
CREATE TABLE dbo.totherProcessSettings (NAME nvarchar(200) NOT NULL, VALUE nvarchar(400) NOT NULL, settingtype nvarchar(20) NOT NULL, LISTVALUE nvarchar(1600) NULL, workercode nvarchar(100) NOT NULL, ordernr int NOT NULL);
GO

INSERT INTO dbo.tprocProcessSettings VALUES (N'Function.Barcode.Parser', N'Parserbuilder', N'Setting', NULL, N'ALL', 10);
INSERT INTO dbo.tprocDocumentItemsListLayout VALUES (N'Column', N'QTY', N'SettingGroup', NULL, N'ALL', 10);
INSERT INTO dbo.totherProcessSettings VALUES (N'Function.OnStart', N'Documents', N'Setting', NULL, N'ALL', 10);

INSERT INTO dbo.tprocDocumentListLayout (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES
 (N'Column',               N'DOCNR',    N'SettingGroup',  NULL, N'ALL',        10),
 (N'Column.DOCNR.Caption', N'Dok nr',   N'Setting',       NULL, N'ALL',        20),
 (N'Column',               N'DATE',     N'SettingGroup',  NULL, N'ALL',        30),
 (N'Column.DATE.Caption',  N'Kuupäev',  N'Setting',       NULL, N'ALL',        40),
 (N'Grid.PageSize',        N'20',       N'Setting',       NULL, N'ALL',        50),
 (N'Grid.Empty',           N'',         N'Setting',       NULL, N'ALL',        60),
 (N'Grid.Secret',          N'abc',      N'ServerSetting', NULL, N'ALL',        70),
 (N'Column.DOCNR.Caption', N'Legacy',   N'Setting',       NULL, N'wterminal2', 20);

INSERT INTO dbo.tprocDocumentListLayoutwterminal2 (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES
 (N'Column.DOCNR.Caption', N'Doc no (env)', N'Setting',      NULL, N'wterminal2', 10),
 (N'Column',               N'STATUS',       N'SettingGroup', NULL, N'wterminal2', 20),
 (N'Column',               N'DOCNR',        N'SettingGroup', NULL, N'wterminal2', 25),
 (N'Grid.ShowFilterRow',   N'true',         N'Setting',      NULL, N'wterminal2', 30),
 (N'Ignored.AllRow',       N'x',            N'Setting',      NULL, N'ALL',        40);

INSERT INTO dbo.tprocDocumentListLayouten (NAME, VALUE, settingtype, LISTVALUE, workercode, ordernr) VALUES
 (N'Column.DATE.Caption', N'Date',  N'Setting', NULL, N'ALL', 10),
 (N'Grid.ShowFilterRow',  N'false', N'Setting', NULL, N'ALL', 20),
 (N'Lang.Only',           N'x',     N'Setting', NULL, N'ALL', 30);

INSERT INTO dbo.ServerProcessSettings (NAME, VALUE, workercode, ordernr, settingtype, LISTVALUE) VALUES
 (N'tproc.Document.State.Terminal.New',        N'NEW',        N'ALL',     0, N'Setting',       NULL),
 (N'tproc.Document.Query.Join',                N'True',       N'ALL',     0, N'Setting',       NULL),
 (N'tproc.Document.Query.Join',                N'False',      N'worker1', 0, N'Setting',       NULL),
 (N'tproc.Unused.Setting',                     N'1',          N'ALL',     0, N'Setting',       NULL),
 (N'tproc.Directo.ApiKey',                     N'secret-key', N'ALL',     0, N'ServerSetting', NULL),
 (N'tprocvariant.Document.State.Terminal.New', N'VNEW',       N'ALL',     0, N'Setting',       NULL),
 (N'tother.Other.Setting',                     N'o',          N'ALL',     0, N'Setting',       NULL);
GO

CREATE PROCEDURE dbo.GettprocDocumentItemsListLayoutInfo @workercode nvarchar(250), @objectname nvarchar(100), @objectkey nvarchar(100)
AS SELECT 1 AS x;
GO

CREATE PROCEDURE dbo.[GettprocProcessSettingsInfo-] @workercode nvarchar(250)
AS SELECT 1 AS x;
GO

CREATE PROCEDURE dbo.GetDocumentsFortproc @workercode nvarchar(250)
AS
BEGIN
    declare @psv dbo.processSettings
    insert into @psv select * from dbo.GetProcessSettingValues('tproc', @workercode)
    declare @new nvarchar(30) = dbo.GetPSV(@psv, 'Document.State.Terminal.New', 'UUS')
    declare @join nvarchar(10) = dbo.GetPSVB(@psv,'Document.Query.Join','False')
    declare @dyn nvarchar(30) = dbo.GetPSV(@psv, @new, 'x')
    select @new, @join, @dyn
END
GO

CREATE PROCEDURE dbo.RegDocStateFortprocFinished @workercode nvarchar(250)
AS
BEGIN
    declare @remove nvarchar(10) = dbo.GetProcessSettingValue('tproc','Document.RegDocState.RemoveNoQty',@workercode,'False')
    declare @key nvarchar(400) = (select top 1 VALUE from dbo.ServerProcessSettings where NAME = 'tproc.Directo.ApiKey')
    select @remove, @key
END
GO

CREATE PROCEDURE dbo.GetDocumentsFortother @workercode nvarchar(250)
AS
BEGIN
    declare @psv dbo.processSettings
    insert into @psv select * from dbo.GetProcessSettingValues('tother', @workercode)
    select dbo.GetPSV(@psv, 'Other.Setting', 'o')
END
GO
