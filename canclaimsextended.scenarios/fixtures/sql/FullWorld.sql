-- Source of fixtures/FullWorld/canclaimsextended.db (PersistenceScenarios, Y1).
-- Turned into the .db by FixtureGenerator; see fixtures/FullWorld/README.md.
-- Every value here is listed in FullWorld (FixtureGenerator.cs): change both together.
--
-- Schema: what SQLiteDatabaseHandler creates today (SQLiteTables + EnsureColumn + plots group
-- tables), user_version = SQLiteTables.SCHEMA_VERSION (101). The generator compares the table and
-- column names with a database the running mod has just created and fails on a difference.

CREATE TABLE CITIES(guid TEXT PRIMARY KEY NOT NULL, name TEXT, mayor TEXT DEFAULT '', timestampcreated INTEGER DEFAULT 0,
    perms TEXT DEFAULT '', opencity INTEGER DEFAULT 0, invmsg TEXT DEFAULT '', bonusplots INTEGER DEFAULT 0,
    bonuschunks INTEGER DEFAULT 0, istechnical INTEGER DEFAULT 0, citycolor INTEGER DEFAULT 0, debtbalance REAL DEFAULT 0,
    fee INTEGER DEFAULT 0, ranks TEXT DEFAULT '', emblem TEXT DEFAULT '', dailymsg TEXT DEFAULT '');
CREATE TABLE PLAYERS(uid TEXT PRIMARY KEY NOT NULL, name TEXT, timestampfirstjoined INTEGER DEFAULT 0, timestamplastonline INTEGER DEFAULT 0,
    city TEXT DEFAULT '', friends TEXT DEFAULT '', perms TEXT DEFAULT '', citytitles TEXT DEFAULT '',
    prefix TEXT DEFAULT '', aftername TEXT DEFAULT '');
CREATE TABLE PLOTS(guid TEXT PRIMARY KEY NOT NULL, name TEXT DEFAULT '', city TEXT DEFAULT '', owner TEXT DEFAULT '',
    type INTEGER DEFAULT 0, price INTEGER DEFAULT -1, customtax REAL DEFAULT 0, perms TEXT DEFAULT '',
    timestampclaimed INTEGER DEFAULT 0, plotdesc TEXT DEFAULT '', number INTEGER DEFAULT 0);
CREATE TABLE PLOTBOXES(plotguid TEXT NOT NULL, idx INTEGER NOT NULL, minx INTEGER, miny INTEGER, minz INTEGER,
    maxx INTEGER, maxy INTEGER, maxz INTEGER, PRIMARY KEY(plotguid, idx));
CREATE TABLE WORLDFLAGS(flag TEXT PRIMARY KEY NOT NULL, value INTEGER DEFAULT 0);
CREATE TABLE INVITATIONS(sendertype TEXT NOT NULL, senderguid TEXT NOT NULL, receivertype TEXT NOT NULL, receiverguid TEXT NOT NULL,
    expire INTEGER DEFAULT 0, PRIMARY KEY(sendertype, senderguid, receivertype, receiverguid));
CREATE TABLE CITYCHUNKS(cityguid TEXT NOT NULL, cx INTEGER NOT NULL, cz INTEGER NOT NULL, timestampclaimed INTEGER DEFAULT 0,
    extrabought INTEGER DEFAULT 0, outpost INTEGER DEFAULT 0, groupguid TEXT DEFAULT '', PRIMARY KEY(cx, cz));
CREATE TABLE CITYLOG(cityguid TEXT NOT NULL, ts INTEGER NOT NULL, event TEXT NOT NULL, args TEXT DEFAULT '');
CREATE TABLE PRISONERS(uid TEXT PRIMARY KEY NOT NULL, plotguid TEXT NOT NULL, releaseat INTEGER DEFAULT 0);
CREATE TABLE CRIMINALS(cityguid TEXT NOT NULL, uid TEXT NOT NULL, PRIMARY KEY(cityguid, uid));
CREATE TABLE PLOTEXTRAS (plotguid TEXT PRIMARY KEY, groupguid TEXT DEFAULT '');
CREATE TABLE PLOTSGROUPS (guid TEXT PRIMARY KEY, city TEXT, name TEXT, perms TEXT, fee REAL DEFAULT 0, pendingfee REAL DEFAULT -1,
    pendingat INTEGER DEFAULT 0, accepted TEXT DEFAULT '');
CREATE TABLE PLOTSGROUPMEMBERS (groupguid TEXT, uid TEXT, PRIMARY KEY (groupguid, uid));

-- The city: every column set to something that is not its default.
INSERT INTO CITIES(guid, name, mayor, timestampcreated, perms, opencity, invmsg, bonusplots, bonuschunks, istechnical,
    citycolor, debtbalance, fee, ranks, emblem, dailymsg)
VALUES('c-full-0001', 'Persistburg', 'u-pmayor', 1700000000, 'citizen:use;citizen:build;friend:use;', 1, 'Welcome', 3, 4, 0,
    -16744448, 12.5, 7,
    '{"Elder":{"Name":"Elder","Permissions":["CITY_INVITE","CITY_SEE_LOG"],"CitizensNames":["pcitizen"]}}',
    'color_red;cross_white', 'Pay your fee');

-- Mayor, citizen (a friend of the mayor, holds the rank, has titles), a criminal in prison, an invited player.
INSERT INTO PLAYERS(uid, name, timestampfirstjoined, timestamplastonline, city, friends, perms, citytitles, prefix, aftername) VALUES
    ('u-pmayor',   'pmayor',   1700000000, 1700001000, 'c-full-0001', '',         '', '',      '',    ''),
    ('u-pcitizen', 'pcitizen', 1700000400, 1700001100, 'c-full-0001', 'u-pmayor', '', 'Elder', 'Sir', 'the Bold'),
    ('u-poutlaw',  'poutlaw',  1700000700, 1700001200, '',            '',         '', '',      '',    ''),
    ('u-pinvitee', 'pinvitee', 1700000800, 1700001300, '',            '',         '', '',      '',    '');

-- Territory: the home chunk, an extra chunk in the plots group, an outpost. Chunks are 16 blocks.
INSERT INTO CITYCHUNKS(cityguid, cx, cz, timestampclaimed, extrabought, outpost, groupguid) VALUES
    ('c-full-0001', 100, 100, 1700000100, 0, 0, ''),
    ('c-full-0001', 101, 100, 1700000200, 1, 0, 'g-full-0001'),
    ('c-full-0001', 106, 100, 1700000300, 0, 1, '');

-- Plot #1: a temple of two touching boxes in chunk 100,100, owned by the citizen, for sale, taxed,
-- its own rights and the pvp flag, in the plots group.
INSERT INTO PLOTS(guid, name, city, owner, type, price, customtax, perms, timestampclaimed, plotdesc, number)
VALUES('p-full-0001', 'Shrine', 'c-full-0001', 'u-pcitizen', 6, 40, 5.5, 'pvp;citizen:use;citizen:build;friend:use;',
    1700000450, '1602,61,1602', 1);
INSERT INTO PLOTBOXES(plotguid, idx, minx, miny, minz, maxx, maxy, maxz) VALUES
    ('p-full-0001', 0, 1601, 60, 1601, 1605, 70, 1605),
    ('p-full-0001', 1, 1605, 60, 1601, 1609, 70, 1605);

-- Plot #2: the prison, two cells, one box.
INSERT INTO PLOTS(guid, name, city, owner, type, price, customtax, perms, timestampclaimed, plotdesc, number)
VALUES('p-full-0002', 'Jail', 'c-full-0001', '', 9, -1, 0, 'citizen:use;', 1700000460, '1602,61,1610;1604,61,1612', 2);
INSERT INTO PLOTBOXES(plotguid, idx, minx, miny, minz, maxx, maxy, maxz) VALUES
    ('p-full-0002', 0, 1601, 60, 1609, 1607, 66, 1615);

-- The outlaw is a criminal of the city and serves in its prison until 2100.
INSERT INTO CRIMINALS(cityguid, uid) VALUES('c-full-0001', 'u-poutlaw');
INSERT INTO PRISONERS(uid, plotguid, releaseat) VALUES('u-poutlaw', 'p-full-0002', 4102444800);

-- A plots group with the citizen in it and a raise that waits until 2100, accepted by the citizen.
INSERT INTO PLOTSGROUPS(guid, city, name, perms, fee, pendingfee, pendingat, accepted)
VALUES('g-full-0001', 'c-full-0001', 'Quarter', 'citizen:use;citizen:build;', 3, 7, 4102444800, 'u-pcitizen');
INSERT INTO PLOTSGROUPMEMBERS(groupguid, uid) VALUES('g-full-0001', 'u-pcitizen');
INSERT INTO PLOTEXTRAS(plotguid, groupguid) VALUES('p-full-0001', 'g-full-0001');

-- The city invited pinvitee; runs out in 2100.
INSERT INTO INVITATIONS(sendertype, senderguid, receivertype, receiverguid, expire)
VALUES('city', 'c-full-0001', 'player', 'u-pinvitee', 4102444800);

-- City log, oldest first; args are separated by char 31.
INSERT INTO CITYLOG(cityguid, ts, event, args) VALUES
    ('c-full-0001', 1700000000, 'CityCreated',   'pmayor' || char(31) || 'Persistburg'),
    ('c-full-0001', 1700000500, 'CitizenJoined', 'pcitizen'),
    ('c-full-0001', 1700000600, 'FeeChanged',    'pmayor' || char(31) || '7');

-- World flags: pvp forbidden and fire everywhere on, the rest off.
INSERT INTO WORLDFLAGS(flag, value) VALUES
    ('pvpew', 0), ('fireew', 1), ('blastew', 0), ('mobspawnew', 0),
    ('pvpfb', 1), ('firefb', 0), ('blastfb', 0), ('mobspawnfb', 0);

PRAGMA user_version = 101;
