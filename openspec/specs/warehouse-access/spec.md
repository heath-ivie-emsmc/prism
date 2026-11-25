## Purpose
Prism reads the shared data warehouse and never changes it. This capability characterizes how that is enforced today: a single access abstraction, a SQL policy, a read-only transaction that is always rolled back, a startup check of the database login, confinement of the warehouse connection string, bounds on every query, and the Data Through load date. The database login holding SELECT only is the real guarantee; the other layers stop Prism's own mistakes and make a misconfigured login visible. Derived from ADR 0008, the warehouse guide, AGENTS.md sections 4 and 9, and the architecture, policy and integration tests.

## Requirements

### Requirement: Single warehouse access abstraction
Every read of the warehouse by Prism SHALL go through one access abstraction. A query SHALL be a single SELECT or WITH statement that filters on the Provider parameter in code (not in a literal or comment), SHALL pass that parameter, and SHALL pass only parameters the query text uses. A query that breaks any rule MUST be refused before it reaches the database. Values SHALL be bound parameters, never concatenated into the text.

#### Scenario: Well-formed query returns only the requested Provider
- **GIVEN** a query that filters on the Provider parameter and binds it
- **WHEN** it runs
- **THEN** only that Provider's rows are returned and another Provider's rows are never returned

#### Scenario: Missing Provider filter refused
- **GIVEN** a query that does not use the Provider parameter, or does not pass it
- **WHEN** it is submitted
- **THEN** it is refused before reaching the database

#### Scenario: Provider parameter hidden in a comment or literal
- **GIVEN** a query whose only mention of the Provider parameter is inside a comment or string literal
- **WHEN** it is submitted
- **THEN** it is refused as not filtering on the Provider

#### Scenario: Multiple statements and unused parameters refused
- **GIVEN** a query containing a semicolon, not starting with SELECT or WITH, empty, over the length cap, or passing a parameter its text does not use
- **WHEN** it is submitted
- **THEN** it is refused before reaching the database

#### Scenario: Warehouse not configured
- **GIVEN** no warehouse connection is configured (Development without the secret)
- **WHEN** any query is requested
- **THEN** the answer says the warehouse is unavailable and the rest of the application is unaffected

### Requirement: SQL policy refuses writes and unsafe constructs
Every warehouse query SHALL pass a SQL policy that reads the text the way PostgreSQL does. The policy MUST refuse write, DDL and administrative verbs, `INTO`, locking clauses, and calls to side-effect or outside-reaching functions, including schema-qualified, quoted and spaced forms. It MUST ignore string literals (plain, escaped and dollar-quoted), quoted identifiers and comments when looking for verbs. It MUST refuse text it cannot read with certainty.

#### Scenario: Data-modifying statement inside a read
- **GIVEN** a query that wraps a DELETE, INSERT or UPDATE in a WITH clause, or follows a SELECT with DDL or a grant
- **WHEN** it is submitted
- **THEN** it is refused

#### Scenario: Side-effect function refused in every spelling
- **GIVEN** a query calling a function that changes session state, terminates a backend, advances a sequence, reaches outside the database, reads server files, sleeps, or executes embedded SQL
- **WHEN** it is submitted, including with a schema qualifier, a quoted name or whitespace before the parenthesis
- **THEN** it is refused

#### Scenario: Locking clauses and SELECT INTO refused
- **GIVEN** a query with a locking clause (including comments or tabs between its words, or inside a subquery) or a SELECT INTO
- **WHEN** it is submitted
- **THEN** it is refused

#### Scenario: Administrative and transaction verbs refused
- **GIVEN** a query containing an administrative or session verb in code, such as a transaction-control, copy, vacuum, notify or reset verb
- **WHEN** it is submitted
- **THEN** it is refused

#### Scenario: Verbs inside literals, comments and identifiers are harmless
- **GIVEN** a query where a verb or function name appears only inside a string literal, a dollar-quoted body, a comment (including nested comments), a quoted identifier, or as part of a longer column name
- **WHEN** it is submitted
- **THEN** it is accepted

#### Scenario: Lookalike characters and quote tricks
- **GIVEN** a query using a non-ASCII or control character in code (a lookalike letter or a no-break space), a literal or comment that is closed early to leave a verb in code, or a unicode-escaped identifier
- **WHEN** it is submitted
- **THEN** it is refused

#### Scenario: Unterminated text refused
- **GIVEN** a query with an unterminated string, quoted identifier, dollar quote or comment
- **WHEN** it is submitted
- **THEN** it is refused

#### Scenario: Pathological text scanned in bounded time
- **GIVEN** a very long or deeply nested query text
- **WHEN** the policy scans it
- **THEN** the scan completes in linear time without exhausting resources

#### Scenario: Every query in the code passes the policy
- **GIVEN** every query text in the production code, including the startup check's own catalog queries
- **WHEN** the policy tests run
- **THEN** every one is accepted, and the build fails if one is not

### Requirement: Read-only transaction, always rolled back
Each query SHALL run in an explicit transaction whose first statement makes it read-only and which MUST always be rolled back, never committed. Each warehouse session SHALL also default to read-only and carry a server-side statement timeout that a connection string cannot switch off.

#### Scenario: Server refuses a write past the policy
- **GIVEN** a write that has bypassed the SQL policy
- **WHEN** it runs inside the query transaction
- **THEN** the server refuses it with the read-only-transaction error (SQLSTATE 25006) and nothing is changed

#### Scenario: Session default turned off
- **GIVEN** a statement that tries to switch off the session's read-only default, or a session whose default was never read-only
- **WHEN** a query then runs
- **THEN** the transaction is still read-only and a write is still refused

#### Scenario: Transaction never commits
- **GIVEN** any query, successful or failed
- **WHEN** it finishes
- **THEN** its transaction is rolled back and the connection is returned to the pool

#### Scenario: Write query refused before the database
- **GIVEN** a write submitted through the normal path
- **WHEN** it is validated
- **THEN** the policy refuses it before any connection is used

### Requirement: Startup check refuses a login that can write
When a warehouse connection is configured, the host SHALL, in every environment including Development and Testing, check the database login from the catalog in a read-only transaction before serving, and MUST refuse to start if the login is a superuser, has CREATEROLE, CREATEDB, REPLICATION or BYPASSRLS, is a member of a privileged or write-capable role (even without inheriting its privileges), can create in the database or any non-system schema, owns the database, a schema, a table, a view or a sequence, can write to any table or view (including by a single column) or use a sequence in any non-system schema. A login that cannot be checked, because the warehouse is unreachable or the catalog cannot be read, MUST also refuse to start. The refusal message SHALL name the kind of privilege, object counts and the role name, and MUST NOT contain the connection string, host or password.

#### Scenario: Superuser refused
- **GIVEN** a login that is a superuser
- **WHEN** the host starts
- **THEN** startup fails with the write-protection failure

#### Scenario: SELECT-only login passes
- **GIVEN** a login with SELECT only on the warehouse schemas, owning nothing and unable to create anything
- **WHEN** the host starts
- **THEN** it starts and logs that the login was verified

#### Scenario: Any single write privilege refused
- **GIVEN** a login with INSERT, UPDATE, DELETE, TRUNCATE or a similar privilege on one table, on a view, or on a single column
- **WHEN** the host starts
- **THEN** startup fails

#### Scenario: Create, ownership and sequence rights refused
- **GIVEN** a login that can create in a schema or the database, owns a table or a schema, or can use a sequence
- **WHEN** the host starts
- **THEN** startup fails

#### Scenario: Privileged or write-capable role membership refused
- **GIVEN** a login that is a member of a role with write access, a superuser role, or a privileged role it does not inherit
- **WHEN** the host starts
- **THEN** startup fails

#### Scenario: Administrative attribute refused
- **GIVEN** a login with CREATEROLE, CREATEDB, REPLICATION or BYPASSRLS
- **WHEN** the host starts
- **THEN** startup fails naming the attribute kind

#### Scenario: Unverifiable login refused
- **GIVEN** the warehouse cannot be reached or the catalog cannot be read
- **WHEN** the host starts
- **THEN** startup fails and the message carries no connection string, host or password

#### Scenario: Message never leaks credentials
- **GIVEN** any refusal of the check
- **WHEN** its message is inspected
- **THEN** it contains no connection string, host or password

#### Scenario: Refused in Development and Testing too
- **GIVEN** a Development or Testing host with a login that can write
- **WHEN** the host starts
- **THEN** it refuses to start, as in a deployed environment

#### Scenario: Check itself changes nothing
- **GIVEN** the check is running
- **WHEN** it reads the catalog
- **THEN** it uses catalog queries only, in a read-only transaction, and changes nothing

#### Scenario: Temporary tables do not refuse
- **GIVEN** a SELECT-only login that is allowed temporary tables
- **WHEN** the host starts
- **THEN** it starts, because a temporary table touches no warehouse object

#### Scenario: Not configured skips the check
- **GIVEN** no warehouse connection is configured
- **WHEN** the host starts
- **THEN** the check is skipped, a warning is logged and the host starts

### Requirement: Temporary local override of the login check
A Development or Testing host MAY skip only the login privilege check through a temporary override, which SHALL log an error naming what the check found on every start. The override MUST NOT relax the read-only transaction or the SQL policy. A deployed host MUST refuse to start with the override set.

#### Scenario: Override in a local host
- **GIVEN** a test host with a login that can write and the override set
- **WHEN** it starts
- **THEN** it starts and logs an error, and every query still runs in a read-only transaction

#### Scenario: Override in a deployed host
- **GIVEN** a deployed environment with the override set
- **WHEN** its configuration is validated
- **THEN** the host refuses to start

### Requirement: Warehouse configuration is validated
Warehouse options SHALL be validated at start. The pool size, command timeout and row cap MUST each stay within fixed ranges. A deployed host MUST have a warehouse connection and MUST require an encrypted connection mode, and MUST NOT set the development Provider substitution. An invalid connection string SHALL be reported without echoing it.

#### Scenario: Deployed host without connection
- **GIVEN** a deployed environment with no warehouse connection string
- **WHEN** options are validated
- **THEN** startup fails

#### Scenario: Deployed host with unencrypted connection
- **GIVEN** a deployed environment whose connection string does not require encryption
- **WHEN** options are validated
- **THEN** startup fails

#### Scenario: Malformed connection string
- **GIVEN** a connection string that is not valid
- **WHEN** options are validated
- **THEN** the problem is reported without quoting the string

#### Scenario: Out-of-range limits
- **GIVEN** a pool size, timeout or row cap outside its allowed range
- **WHEN** options are validated
- **THEN** startup fails

### Requirement: Warehouse connection confined to one place
Only the warehouse access code and its single registration point SHALL read the warehouse connection string or open a warehouse connection. No Entity Framework context SHALL be configured with it, and Prism's own database SHALL be a separate connection that never shares the warehouse pool. Tools, scripts, container files and CI configuration MUST NOT reference the warehouse connection.

#### Scenario: Production code elsewhere touches the connection
- **GIVEN** production code outside the warehouse folder and its registration point
- **WHEN** the architecture guard runs
- **THEN** the build fails if it reads the warehouse connection string or opens a connection to it

#### Scenario: EF context on the warehouse
- **GIVEN** any database context in the code
- **WHEN** the architecture guard runs
- **THEN** the build fails if one is configured with the warehouse connection

#### Scenario: Tools and CI
- **GIVEN** tooling, scripts, docker files or CI workflows
- **WHEN** the architecture guard runs
- **THEN** the build fails if any references the warehouse connection

### Requirement: No migrations, SQL files or DDL against the warehouse
Prism SHALL have no migration, migration builder or `.sql` file targeting the warehouse schemas, and no production SQL with DDL or DML against them. A change to the warehouse MUST be a request to the database team, never a code change.

#### Scenario: Migration or SQL file targets the warehouse
- **GIVEN** the repository
- **WHEN** the architecture guard runs
- **THEN** the build fails if a migration or `.sql` file targets a warehouse schema

#### Scenario: Production SQL with DDL or DML
- **GIVEN** production SQL text that creates, alters, drops, inserts, updates or deletes against a warehouse schema
- **WHEN** the scan runs
- **THEN** the build fails

#### Scenario: Scan recognises hostile forms and leaves reads alone
- **GIVEN** representative DDL and DML texts, and representative reads
- **WHEN** the scan runs over them
- **THEN** every write form is flagged and every read is left alone, and the scan is proven able to find what it looks for

### Requirement: Tests never reach a real warehouse
Test code SHALL obtain a database connection only from a disposable container database registered by the shared test fixture, and every test helper that creates, drops or writes MUST first assert the connection through a guard that refuses anything not registered by a fixture on a local or private-network host. Tests and tools MUST NOT read the warehouse connection from real configuration, user secrets or the environment, and the test host SHALL blank the warehouse setting. The guard MUST NOT echo a connection string.

#### Scenario: Guard accepts only a registered container
- **GIVEN** a connection string for a registered local container database
- **WHEN** the guard is asked to assert it
- **THEN** it passes

#### Scenario: Guard refuses everything else
- **GIVEN** an unregistered database, a remote host, or a connection string that does not name one host, a port and a database
- **WHEN** the guard is asked to assert it
- **THEN** it refuses and the message does not include the connection string

#### Scenario: Fixture cannot register a remote database
- **GIVEN** a fixture that tries to register a remote database
- **WHEN** it registers
- **THEN** registration is refused

#### Scenario: Developer holds a production connection
- **GIVEN** a developer with a production warehouse connection in user secrets or the environment
- **WHEN** the full test suite runs
- **THEN** no test reads or connects to it, because the test host blanks the setting

#### Scenario: Test opens an unguarded connection
- **GIVEN** a test that builds a database connection with a string that did not pass the guard
- **WHEN** the architecture guard runs
- **THEN** the build fails

### Requirement: Query timeout and result-size cap
Every warehouse query SHALL be cancelled at a time limit (30 seconds by default) by both the client and the server, and SHALL fail rather than return more rows than a result-size cap (10,000 by default). A timeout or connection failure MUST be reported as the warehouse being unavailable, without the SQL, host or connection string. Any other database error SHALL be surfaced as a query bug with only its SQL state logged.

#### Scenario: Slow query cancelled
- **GIVEN** a query that runs longer than the time limit
- **WHEN** the limit passes
- **THEN** it is cancelled and reported as the warehouse being unavailable

#### Scenario: Result over the cap
- **GIVEN** a query that would return more rows than the cap
- **WHEN** it runs
- **THEN** it fails with a result-too-large error instead of returning a truncated or memory-filling list

#### Scenario: Unreachable warehouse
- **GIVEN** a warehouse that cannot be reached
- **WHEN** a query is requested
- **THEN** it reports unavailable and the message carries no connection string

### Requirement: Warehouse queries leave no raw input in logs
Warehouse logging SHALL record only the query name, elapsed time, row count and SQL state. It MUST NOT log SQL text, parameter values or database error messages.

#### Scenario: Finished query log
- **GIVEN** a query that finishes
- **WHEN** its log entry is read
- **THEN** it holds the name, rows and time and not the SQL or any parameter value

#### Scenario: Database error log
- **GIVEN** a query that fails with a database error whose message quotes a value
- **WHEN** the failure is logged
- **THEN** only the name, time and SQL state are logged, never the message

#### Scenario: Timeout log
- **GIVEN** a query that times out
- **WHEN** the timeout is logged
- **THEN** the log names the query, duration and SQL state and nothing else

### Requirement: Warehouse Provider resolved by identity
A Provider in the route (a portal identifier, checked against the user's assignments) SHALL be translated to the warehouse's own Provider identifier through the warehouse's identifier links before every Metric query. A portal identifier with no match or more than one match MUST be treated as unmapped and the queries MUST return nothing; it MUST NOT fall back to the same number. The translation SHALL be cached with a short lifetime and a size cap.

#### Scenario: Mapped Provider
- **GIVEN** a portal Provider with exactly one warehouse match
- **WHEN** a Metric query runs
- **THEN** it is scoped to the matched warehouse Provider

#### Scenario: Unmapped or ambiguous Provider
- **GIVEN** a portal Provider with no warehouse match or several
- **WHEN** a Metric query runs
- **THEN** nothing is returned and the same number is never reused as the warehouse identifier

#### Scenario: Overlapping numbering
- **GIVEN** a portal identifier numerically equal to a different Provider's warehouse identifier
- **WHEN** the translation runs
- **THEN** it returns the matched Provider's warehouse identifier and never the other Provider's data

### Requirement: Data Through is the load date
Prism SHALL show Data Through as the warehouse load date of a Provider's data: a date with no time of day, never described as "last updated". One reader SHALL return the newest load date a Provider has in a source, through the warehouse abstraction with the Provider filter, the standard timeout and the row cap. Every aggregate Metric response SHALL carry it, and it SHALL be null when the Provider has no data in the source. When a Metric reads several load-dated sources it SHALL use the oldest of their dates, and null if any has none. The answer SHALL be cached briefly per warehouse Provider and source under a size cap, judged by the injected clock.

#### Scenario: Newest load date returned
- **GIVEN** a Provider with data in a source
- **WHEN** Data Through is read
- **THEN** the newest load date is returned as a date

#### Scenario: Provider without data
- **GIVEN** a Provider with no data in the source, or an unmapped Provider
- **WHEN** Data Through is read
- **THEN** it is null

#### Scenario: Metric reads two sources
- **GIVEN** a Metric over two load-dated sources with different dates
- **WHEN** Data Through is read
- **THEN** the older date is used, and null if either source has none

#### Scenario: New load appears
- **GIVEN** a cached Data Through
- **WHEN** its brief lifetime passes after a new load
- **THEN** the new date is read

#### Scenario: Cross-Provider
- **GIVEN** two Providers with different loads
- **WHEN** each reads Data Through
- **THEN** each gets only its own date

## Open questions
- The warehouse's identifier links used to map a portal Provider to a warehouse Provider are documented as an agreed chain but are not confirmed by the database team as stable; a change would silently make Providers unmapped.
- Whether a read replica or read-only endpoint will be used so the server refuses writes regardless of login rights; today only the login, the startup check and the read-only transaction protect the warehouse.
- The startup check fails closed, so a restart while the warehouse is down will not come up; whether a bounded retry or a degraded start is acceptable is undecided.
- The login's privileges are verified only at startup, not periodically; a grant added later is not noticed until the next start.
- The policy is stricter than needed (an unquoted column named like a verb is refused); whether that should be relaxed or kept is not decided.
- Temporary tables are tolerated; the database team may choose to revoke the right entirely.
- The temporary override of the login check exists only until a SELECT-only login is issued; its removal date is not set.
