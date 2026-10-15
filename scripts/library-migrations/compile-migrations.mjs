import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { createHash } from 'node:crypto';
import ts from 'typescript';
import { buildSync } from 'esbuild';

/** Compile only the existing declarative SQL subset; never execute migration functions. */
export const compileLibraryMigrations = (projectRoot) => {
  const path = resolve(projectRoot, 'src/main/database/migrations.ts');
  const source = ts.createSourceFile(path, readFileSync(path, 'utf8'), ts.ScriptTarget.Latest, true);
  const imports = source.statements.filter(ts.isImportDeclaration).filter(node => !node.importClause?.isTypeOnly);
  const bindings = [];
  const entry = imports.map((node, index) => {
    const names = node.importClause?.namedBindings;
    if (!names || !ts.isNamedImports(names)) throw new Error('Unsupported migration import.');
    return names.elements.map((element, offset) => {
      const alias = 'value_' + index + '_' + offset;
      bindings.push([element.name.text, alias]);
      return 'import { ' + (element.propertyName?.text ?? element.name.text) + ' as ' + alias + ' } from '
        + JSON.stringify(resolve(dirname(path), node.moduleSpecifier.text)) + ';\nexport { ' + alias + ' };';
    }).join('\n');
  }).join('\n');
  const bundled = buildSync({ stdin: { contents: entry, resolveDir: projectRoot, loader: 'ts' },
    bundle: true, write: false, format: 'cjs', platform: 'node' }).outputFiles[0].text;
  const module = { exports: {} };
  new Function('module', 'exports', bundled)(module, module.exports);
  const values = Object.fromEntries(bindings.map(([name, alias]) => [name, module.exports[alias]]));
  const fail = (node) => { throw new Error('Unsupported migration syntax: ' + ts.SyntaxKind[node.kind]); };
  const unwrap = node => ts.isParenthesizedExpression(node) || ts.isAsExpression(node) ? unwrap(node.expression) : node;
  const value = (node, locals) => {
    node = unwrap(node);
    if (ts.isStringLiteralLike(node)) return node.text;
    if (ts.isIdentifier(node) && typeof locals[node.text] === 'string') return locals[node.text];
    if (ts.isTemplateExpression(node)) return node.head.text + node.templateSpans.map(span =>
      String(value(span.expression, locals)) + span.literal.text).join('');
    if (ts.isArrayLiteralExpression(node)) return node.elements.map(element => value(element, locals));
    return fail(node);
  };
  const callNamed = (node, name) => ts.isCallExpression(node) && ts.isIdentifier(node.expression) && node.expression.text === name;
  const propertyCall = (node, property) => ts.isCallExpression(node) && ts.isPropertyAccessExpression(node.expression)
    && node.expression.name.text === property;
  const predicate = (node, locals) => {
    node = unwrap(node);
    if (ts.isPrefixUnaryExpression(node) && node.operator === ts.SyntaxKind.ExclamationToken)
      return { not: predicate(node.operand, locals) };
    if (callNamed(node, 'hasColumn')) return { column: [value(node.arguments[1], locals), value(node.arguments[2], locals)] };
    if (ts.isIdentifier(node) && locals[node.text]?.existsSql) return { existsSql: locals[node.text].existsSql };
    return fail(node);
  };
  const statements = (node, locals) => {
    if (ts.isBlock(node)) return node.statements.flatMap(statement => statements(statement, locals));
    if (ts.isExpressionStatement(node)) node = node.expression;
    if (propertyCall(node, 'exec') && ts.isIdentifier(node.expression.expression)
      && node.expression.expression.text === 'database') return [{ sql: value(node.arguments[0], locals) }];
    if (callNamed(node, 'addColumnIfMissing')) return [{ addColumn: node.arguments.slice(1).map(arg => value(arg, locals)) }];
    if (ts.isIfStatement(node)) return [{ if: predicate(node.expression, locals),
      then: statements(node.thenStatement, { ...locals }),
      ...(node.elseStatement ? { else: statements(node.elseStatement, { ...locals }) } : {}) }];
    if (ts.isTryStatement(node) && node.catchClause?.block.statements.length === 0 && !node.finallyBlock)
      return [{ optional: statements(node.tryBlock, { ...locals }) }];
    if (ts.isForOfStatement(node) && ts.isVariableDeclarationList(node.initializer)
      && node.initializer.declarations.length === 1 && ts.isIdentifier(node.initializer.declarations[0].name)) {
      const name = node.initializer.declarations[0].name.text;
      return value(node.expression, locals).flatMap(item => statements(node.statement, { ...locals, [name]: item }));
    }
    if (ts.isVariableStatement(node) && node.declarationList.declarations.length === 1) {
      const declaration = node.declarationList.declarations[0];
      const expression = unwrap(declaration.initializer);
      if (ts.isIdentifier(declaration.name) && propertyCall(expression, 'get')
        && propertyCall(expression.expression.expression, 'prepare')) {
        const sql = value(expression.expression.expression.arguments[0], locals);
        if (!/^SELECT\s+1\s+AS\s+present\s+FROM\s+sqlite_master\b/iu.test(sql)) return fail(node);
        locals[declaration.name.text] = { existsSql: sql };
        return [];
      }
    }
    return fail(node);
  };
  const declaration = source.statements.filter(ts.isVariableStatement)
    .flatMap(node => node.declarationList.declarations).find(node => node.name.getText(source) === 'migrations');
  if (!declaration || !ts.isArrayLiteralExpression(declaration.initializer)) throw new Error('Migration array was not found.');
  const migrations = declaration.initializer.elements.map(node => {
    if (!ts.isObjectLiteralExpression(node)) return fail(node);
    const properties = Object.fromEntries(node.properties.filter(ts.isPropertyAssignment).map(property =>
      [property.name.getText(source), property.initializer]));
    if (!ts.isNumericLiteral(properties.id) || !ts.isArrowFunction(properties.apply)) return fail(node);
    return { id: Number(properties.id.text), steps: statements(properties.apply.body, { ...values }) };
  });
  if (migrations.length > 256 || migrations.some((migration, index) => !Number.isSafeInteger(migration.id)
    || migration.id <= (migrations[index - 1]?.id ?? 0))) throw new Error('Migration IDs must be unique and ordered.');
  const catalog = { v: 1, migrationTableSql: values.schemaMigrationTableSql, migrations };
  const digest = createHash('sha256').update(JSON.stringify(catalog)).digest('hex');
  return { ...catalog, digest };
};
