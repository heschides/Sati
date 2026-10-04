"""Offline generator for the reviewed amendment migration's schema guards.

No connection or runtime data is used. Review the output before controlled use.
Only the additive SQL forms in NoteAmendmentsMigration.sql are accepted.
"""
import re
from pathlib import Path

root = Path(__file__).resolve().parent
source = (root / 'NoteAmendmentsMigration.sql').read_text(encoding='utf-8-sig')
bodies = re.findall(r'\nBEGIN\s*\n(.*?)\nEND;', source, re.S)
assert len(bodies) == 23, f'Unexpected migration statement count: {len(bodies)}'
parts = ["""-- Generated offline by Build-NoteAmendmentGuard.py; review before use.
-- Caller owns the transaction, sets @expectedDatabase and holds SatiDemo.FullReset.
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME()<>@expectedDatabase OR NOT EXISTS
 (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id=1 AND EnvironmentName=N'Demo')
 THROW 53600, 'Database identity mismatch.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
 WHERE MigrationId=N'20261002221023_AddScheduledNoteMoves')
 THROW 53601, 'Predecessor migration missing.', 1;
DECLARE @applied bit=CASE WHEN EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
 WHERE MigrationId=N'20261004120026_AddNoteAmendments') THEN 1 ELSE 0 END;
IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory)<>124+CONVERT(int,@applied)
 THROW 53602, 'Unexpected migration boundary.', 1;
"""]

def fail(message):
    return f" THROW 53603, '{message}', 1;\n"

def execute(sql):
    return "EXEC(N'" + sql.strip().replace("'", "''") + "');\n"

def column_check(table, name, typ, nullable, identity):
    base = typ.split('(')[0]
    length = {'bigint':8, 'int':4, 'bit':1, 'uniqueidentifier':16, 'datetime2':8, 'decimal':9}.get(base)
    precision, scale = (18, 2) if base == 'decimal' else (None, 7 if base == 'datetime2' else 0)
    if base == 'nvarchar':
        size = re.search(r'\(([^)]+)\)', typ)[1]
        length = -1 if size == 'max' else 2 * int(size)
    assert length is not None, typ
    extra = f' AND c.precision={precision}' if precision else ''
    return f"""IF NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
 WHERE c.object_id=OBJECT_ID(N'dbo.{table}') AND c.name=N'{name}' AND t.name=N'{base}'
 AND c.max_length={length} AND c.scale={scale} AND c.is_nullable={int(nullable)}
 AND c.is_identity={int(identity)} AND c.is_computed=0 AND c.default_object_id=0{extra})
""" + fail(f'Incompatible column {table}.{name}.')

def key_check(table, name, column):
    return f"""IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.{table}')
 AND i.name=N'{name}' AND i.is_primary_key=1 AND i.is_unique=1 AND i.type=1 AND i.is_disabled=0
 AND (SELECT COUNT(*) FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id)=1
 AND EXISTS (SELECT 1 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id
 AND c.key_ordinal=1 AND c.is_descending_key=0 AND COL_NAME(c.object_id,c.column_id)=N'{column}'))
""" + fail(f'Incompatible primary key {name}.')

def foreign_key_check(table, name, column, reference, referenced_column):
    return f"""IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns c
 ON c.constraint_object_id=f.object_id WHERE f.parent_object_id=OBJECT_ID(N'dbo.{table}')
 AND f.name=N'{name}' AND f.referenced_object_id=OBJECT_ID(N'dbo.{reference}')
 AND f.delete_referential_action=0 AND f.update_referential_action=0
 AND f.is_disabled=0 AND f.is_not_trusted=0
 AND COL_NAME(f.parent_object_id,c.parent_column_id)=N'{column}'
 AND COL_NAME(f.referenced_object_id,c.referenced_column_id)=N'{referenced_column}'
 AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id=f.object_id)=1)
""" + fail(f'Incompatible foreign key {name}.')

for body in bodies:
    sql = body.strip()
    if sql.startswith('ALTER TABLE') and 'ADD CONSTRAINT' not in sql:
        m = re.fullmatch(r'ALTER TABLE \[(\w+)\] ADD \[(\w+)\] ([\w(),]+) NULL;', sql)
        assert m, sql
        table, name, typ = m.groups()
        parts += [f"IF COL_LENGTH(N'dbo.{table}',N'{name}') IS NULL\n" + execute(sql),
                  column_check(table, name, typ, True, False)]
    elif sql.startswith('CREATE TABLE'):
        table = re.search(r'CREATE TABLE \[(\w+)\]', sql)[1]
        cols = re.findall(r'^\s*\[(\w+)\] ([\w(),]+) (NOT NULL|NULL)( IDENTITY)?[,\n]', sql, re.M)
        assert cols, sql
        parts += [f"IF OBJECT_ID(N'dbo.{table}') IS NOT NULL AND OBJECT_ID(N'dbo.{table}',N'U') IS NULL\n" + fail('Table name occupied.'),
                  f"IF OBJECT_ID(N'dbo.{table}',N'U') IS NULL\n" + execute(sql),
                  f"IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.{table}'))<>{len(cols)}\n" + fail(f'Unexpected columns in {table}.')]
        for name, typ, null, ident in cols:
            parts.append(column_check(table,name,typ,null=='NULL',bool(ident)))
            if ident:
                parts.append(f"IF IDENT_SEED(N'dbo.{table}')<>1 OR IDENT_INCR(N'dbo.{table}')<>1\n" + fail('Unexpected identity seed.'))
        pk, pkcol = re.search(r'CONSTRAINT \[(\w+)\] PRIMARY KEY \(\[(\w+)\]\)',sql).groups()
        parts.append(key_check(table,pk,pkcol))
        fks = re.findall(r'CONSTRAINT \[(\w+)\] FOREIGN KEY \(\[(\w+)\]\) REFERENCES \[(\w+)\] \(\[(\w+)\]\)',sql)
        for name,col,ref,refcol in fks:
            parts.append(foreign_key_check(table,name,col,ref,refcol))
        parts.append(f"IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.{table}'))<>{len(fks)}\n" + fail('Unexpected table relationships.'))
    elif sql.startswith('CREATE') or sql.startswith("EXEC(N'CREATE"):
        if sql.startswith('EXEC'):
            sql = sql.removesuffix(';')[len("EXEC(N'"):-2]
        m = re.fullmatch(r'CREATE (UNIQUE )?INDEX \[(\w+)\] ON \[(\w+)\] \((.+?)\)(?: WHERE (.+?))?;?', sql)
        assert m, sql
        unique,name,table,cols,flt=m.groups()
        columns=','.join(re.findall(r'\[(\w+)\]',cols))
        norm = lambda s: re.sub(r'[\s\[\]()]','',s or '').lower()
        filter_condition = 'i.has_filter=0' if not flt else "i.has_filter=1 AND " + "LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(i.filter_definition,' ',''),'[',''),']',''),'(',''),')',''))=N'" + norm(flt) + "'"
        if flt:
            assert norm(flt) == 'statusin0,1,2', repr(flt)
            filter_condition = "i.has_filter=1 AND LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(i.filter_definition,' ',''),'[',''),']',''),'(',''),')','')) IN (N'statusin0,1,2',N'status=0orstatus=1orstatus=2',N'status=2orstatus=1orstatus=0')"
        parts += [f"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.{table}') AND name=N'{name}')\n" + execute(sql),
        f"""IF NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.{table}') AND i.name=N'{name}'
 AND i.is_unique={int(bool(unique))} AND i.type=2 AND i.is_disabled=0 AND i.is_hypothetical=0 AND {filter_condition}
 AND (SELECT STRING_AGG(CONVERT(nvarchar(max),COL_NAME(c.object_id,c.column_id)),N',') WITHIN GROUP(ORDER BY c.key_ordinal)
 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id AND c.key_ordinal>0)=N'{columns}'
 AND NOT EXISTS (SELECT 1 FROM sys.index_columns c WHERE c.object_id=i.object_id AND c.index_id=i.index_id
 AND (c.is_included_column=1 OR c.is_descending_key=1)))
""" + fail(f'Incompatible index {name}.')]
    elif sql.startswith('ALTER TABLE'):
        table,name,col,ref,refcol=re.fullmatch(r'ALTER TABLE \[(\w+)\] ADD CONSTRAINT \[(\w+)\] FOREIGN KEY \(\[(\w+)\]\) REFERENCES \[(\w+)\] \(\[(\w+)\]\) ON DELETE NO ACTION;',sql).groups()
        parts += [f"IF OBJECT_ID(N'dbo.{name}',N'F') IS NULL\n" + execute(sql), foreign_key_check(table,name,col,ref,refcol)]
    elif sql.startswith('INSERT INTO'):
        parts += ['IF @applied=0\n' + sql + '\n',
                  "IF (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory)<>125\n" + fail('Migration count verification failed.')]
    else:
        raise ValueError(sql)

(root/'Apply-NoteAmendmentsMigration.guarded.sql').write_text('\n'.join(parts),encoding='utf-8')
print('Generated guarded additive amendment migration; no database accessed.')
