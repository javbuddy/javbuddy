using Microsoft.Data.Sqlite;

namespace Javbuddy.Services.R18Dev;

/// <summary>Resolves the file path for the r18.dev dump's local SQLite side-database — a sibling
/// of the main DB (see ConnectionStrings:Default), never referenced by the main AppDbContext.
/// Purely a regenerable cache: deleting it and re-importing is always safe, same as the main DB
/// per CLAUDE.md's "recreate from scratch" guidance.</summary>
public static class R18DevDumpPaths
{
    public static string GetDbPath(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default") ?? "Data Source=Javbuddy.db";
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        var fullDataSourcePath = Path.GetFullPath(dataSource);
        var directory = Path.GetDirectoryName(fullDataSourcePath) ?? ".";
        var fileName = Path.GetFileNameWithoutExtension(fullDataSourcePath) + ".r18dev.db";
        return Path.Combine(directory, fileName);
    }
}
