-- Creates the fla_ SpaceParts source database: raw tables in [data] shaped like the public CSVs,
-- and the Dimview/Factview views that the template model's M partitions read.
-- Run through load-sql.ps1, which checks the fla_ prefix and supplies $(DatabaseName).
:on error exit
USE master;
IF DB_ID(N'$(DatabaseName)') IS NOT NULL
BEGIN
    ALTER DATABASE [$(DatabaseName)] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$(DatabaseName)];
END
CREATE DATABASE [$(DatabaseName)];
ALTER DATABASE [$(DatabaseName)] SET RECOVERY SIMPLE;
GO
USE [$(DatabaseName)];
GO
-- The SSAS copy processes as its service account (impersonateServiceAccount); give it read access to this database only.
IF SUSER_ID(N'NT Service\MSSQLServerOLAPService') IS NOT NULL
BEGIN
    CREATE USER [NT Service\MSSQLServerOLAPService] FOR LOGIN [NT Service\MSSQLServerOLAPService];
    ALTER ROLE db_datareader ADD MEMBER [NT Service\MSSQLServerOLAPService];
END
GO
CREATE SCHEMA data;
GO
CREATE SCHEMA Dimview;
GO
CREATE SCHEMA Factview;
GO

-- Column order matches each CSV header exactly, so BULK INSERT maps by position.
CREATE TABLE data.Brands ([Flagship] nvarchar(100), [Class] nvarchar(100), [Type] nvarchar(100), [Brand] nvarchar(100),
    [Sub Brand] nvarchar(100), [Product Brand VP] nvarchar(100));
CREATE TABLE data.BudgetRate ([Rate] float, [From Currency] nvarchar(20), [To Currency] nvarchar(20), [Currency System] nvarchar(100));
CREATE TABLE data.Customers ([Customer Key] nvarchar(20), [Customer Sold-To Name] nvarchar(200), [Account Name] nvarchar(200),
    [Key Account Name] nvarchar(200), [Transaction Type] nvarchar(50), [Account Type] nvarchar(50),
    [Key Account Manager] nvarchar(100), [Account Manager] nvarchar(100), [Station] nvarchar(100));
CREATE TABLE data.Employees ([Role] nvarchar(100), [Employee Name] nvarchar(100), [Employee Email] nvarchar(200), [Data Security Rule] nvarchar(400));
CREATE TABLE data.ExchangeRate ([Rate Type] nvarchar(50), [From Currency] nvarchar(20), [To Currency] nvarchar(20), [Currency System] nvarchar(100),
    [Rate] float, [Date] date, [Month] nvarchar(20), [Exchange Rate Composite Key] nvarchar(100));
CREATE TABLE data.InvoiceDocumentType ([Billing Document Type Code] nvarchar(10), [Text] nvarchar(100), [Doc. Type Ordinal] bigint,
    [Group] nvarchar(100), [Group Ordinal] bigint);
CREATE TABLE data.OrderDocumentType ([Sales Order Document Type Code] nvarchar(10), [Text] nvarchar(100), [Doc. Type Ordinal] bigint,
    [Group] nvarchar(100), [Group Ordinal] bigint);
CREATE TABLE data.OrderStatus ([Order Status Code] nvarchar(50), [Order Status Text] nvarchar(100), [Order Status Ordinal] bigint,
    [Order Status Group] nvarchar(100), [Order Status Grouping Ordinal] bigint);
CREATE TABLE data.Products ([Sub Brand Name] nvarchar(100), [Ship Class for Part] nvarchar(100), [Product Name] nvarchar(200),
    [Product Business Line Leader] nvarchar(100), [Part Fit Grading] nvarchar(100), [Product Key] bigint, [Subtype] nvarchar(100),
    [Type] nvarchar(100), [Weight (Tonnes)] float, [Maximum Temperature (K)] bigint, [Velocity Tolerance (Meters / Second)] bigint,
    [Tolerance (g)] bigint, [MK] nvarchar(20), [Color] nvarchar(50), [Production Series] nvarchar(100), [Nameplate] nvarchar(200),
    [Material] nvarchar(400));
CREATE TABLE data.Regions ([All] nvarchar(20), [System] nvarchar(100), [Interplanetary Region] nvarchar(100), [Territory] nvarchar(100),
    [Station] nvarchar(100), [Station Type] nvarchar(50), [Tax Rate] float, [System Sales Directors] nvarchar(100),
    [Station Sales Managers] nvarchar(100), [System Regional Managers] nvarchar(100), [Territory Directors] nvarchar(100));
CREATE TABLE data.Budget ([Month] date, [Total Budget] float, [Customer Key] nvarchar(20), [Product Key] bigint, [DWCreatedDate] datetime2(3));
CREATE TABLE data.Forecast ([Forecast Month] date, [Region Territory] nvarchar(100), [Product Type] nvarchar(100), [Forecast (EUR)] float,
    [DWCreatedDate] datetime2(3));
CREATE TABLE data.Orders ([Sales Order Document Number] bigint, [Order Date] date, [Customer Key] nvarchar(20),
    [Sales Order Document Line Item Number] bigint, [Product Key] bigint, [Billing Date] date, [Ship Date] date,
    [Request Goods Receipt Date] date, [Confirm Goods Receipt Date] date, [Sales Order Document Type Code] nvarchar(10),
    [Sales Order Document Line Item Status] nvarchar(50), [Local Currency] nvarchar(20), [Net Order Value] float,
    [Net Order Quantity] bigint, [DWCreatedDate] datetime2(3));
CREATE TABLE data.Invoices ([Billing Document Number] bigint, [Billing Date] date, [Customer Key] nvarchar(20),
    [Billing Document Line Item Number] bigint, [Product Key] bigint, [Ship Date] date, [OTD Indicator] nvarchar(5),
    [Billing Document Type Code] nvarchar(10), [Local Currency] nvarchar(20), [Delivery Cost] float, [Net Invoice COGS] float,
    [Late Delivery Penalties] float, [Overdue Payment Penalties] float, [Taxes & Commercial Fees] float, [Freight] float,
    [Net Invoice Cost] float, [Net Invoice Value] float, [Net Invoice Quantity] bigint, [DWCreatedDate] datetime2(3));
GO

-- Views expose exactly the model's source column names (template-TMSL model.bim).
CREATE VIEW Dimview.[Brands] AS SELECT [Flagship], [Class], [Type], [Brand], [Sub Brand], [Product Brand VP] FROM data.Brands;
GO
CREATE VIEW Dimview.[Budget Rate] AS SELECT [Rate], [From Currency], [To Currency], [Currency System] FROM data.BudgetRate;
GO
CREATE VIEW Dimview.[Customers] AS SELECT [Customer Key], [Customer Sold-To Name], [Account Name], [Key Account Name], [Transaction Type],
    [Account Type], [Station], [Account Manager], [Key Account Manager] FROM data.Customers;
GO
CREATE VIEW Dimview.[Employees] AS SELECT [Role], [Employee Name], [Employee Email], [Data Security Rule] FROM data.Employees;
GO
CREATE VIEW Dimview.[Exchange Rate] AS SELECT [Rate Type], [From Currency], [To Currency], [Currency System], [Rate],
    CAST([Date] AS datetime2(0)) AS [Date], [Month], [Exchange Rate Composite Key] FROM data.ExchangeRate;
GO
CREATE VIEW Dimview.[Invoice Document Type] AS SELECT [Billing Document Type Code], [Text], [Doc. Type Ordinal], [Group], [Group Ordinal] FROM data.InvoiceDocumentType;
GO
CREATE VIEW Dimview.[Order Document Type] AS SELECT [Sales Order Document Type Code], [Text], [Doc. Type Ordinal], [Group], [Group Ordinal] FROM data.OrderDocumentType;
GO
CREATE VIEW Dimview.[Order Status] AS SELECT [Order Status Code], [Order Status Text], [Order Status Ordinal], [Order Status Group],
    [Order Status Grouping Ordinal] FROM data.OrderStatus;
GO
CREATE VIEW Dimview.[Products] AS SELECT [Sub Brand Name], [Ship Class for Part], [Product Name], [Part Fit Grading], [Product Key], [Subtype],
    [Type], [MK], [Color], [Production Series], [Nameplate], [Material], [Maximum Temperature (K)], [Product Business Line Leader],
    [Tolerance (g)], [Velocity Tolerance (Meters / Second)], [Weight (Tonnes)] FROM data.Products;
GO
CREATE VIEW Dimview.[Regions] AS SELECT [System], [Interplanetary Region], [Territory], [Station], [Station Type], [Territory Directors], [All],
    [Station Sales Managers], [System Regional Managers], [System Sales Directors], [Tax Rate] FROM data.Regions;
GO
CREATE VIEW Factview.[Budget] AS SELECT CAST([Month] AS datetime2(0)) AS [Month], [Total Budget] AS [Budget (EUR)], [Customer Key], [Product Key] FROM data.Budget;
GO
CREATE VIEW Factview.[Forecast] AS SELECT CAST([Forecast Month] AS datetime2(0)) AS [Forecast Month], [Region Territory], [Product Type],
    CAST(ROUND([Forecast (EUR)], 0) AS bigint) AS [Forecast (EUR)] FROM data.Forecast;
GO
CREATE VIEW Factview.[Orders] AS SELECT [Sales Order Document Number], CAST([Order Date] AS datetime2(0)) AS [Order Date], [Customer Key],
    [Sales Order Document Line Item Number], [Product Key], CAST([Billing Date] AS datetime2(0)) AS [Billing Date],
    CAST([Ship Date] AS datetime2(0)) AS [Ship Date], CAST([Request Goods Receipt Date] AS datetime2(0)) AS [Request Goods Receipt Date],
    CAST([Confirm Goods Receipt Date] AS datetime2(0)) AS [Confirm Goods Receipt Date], [Sales Order Document Type Code],
    [Sales Order Document Line Item Status], [Local Currency], [Net Order Value], [Net Order Quantity] FROM data.Orders;
GO
CREATE VIEW Factview.[Invoices] AS SELECT [Billing Document Number], CAST([Billing Date] AS datetime2(0)) AS [Billing Date], [Customer Key],
    [Billing Document Line Item Number], [Product Key], CAST([Ship Date] AS datetime2(0)) AS [Ship Date],
    CAST(CASE [OTD Indicator] WHEN N'True' THEN 1 ELSE 0 END AS bit) AS [OTD Indicator], [Billing Document Type Code], [Local Currency],
    [Delivery Cost], [Net Invoice COGS], [Late Delivery Penalties], [Overdue Payment Penalties], [Taxes & Commercial Fees], [Freight],
    [Net Invoice Value], [Net Invoice Quantity] FROM data.Invoices;
GO
