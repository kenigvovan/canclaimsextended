-- Source of fixtures/BrokenRows/canclaimsextended.db (DbLoadScenarios, Y2).
-- Turned into the .db by FixtureGenerator; see fixtures/FullWorld/README.md.
-- Every value here is listed in BrokenRows (FixtureGenerator.cs): change both together.
--
-- A current-format file (user_version 101) edited by hand: next to good rows, a plot without boxes,
-- two plots that overlap, and a chunk of a city that does not exist. Chunk 400,400 is blocks
-- 6400..6415 on x and z.

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

INSERT INTO CITIES(guid, name, mayor, timestampcreated, perms, fee, ranks, emblem, dailymsg)
VALUES('c-bad-0001', 'Brokenburg', 'u-bmayor', 1650000000, 'citizen:use;citizen:build;', 0, '', '', '');
INSERT INTO PLAYERS(uid, name, timestampfirstjoined, timestamplastonline, city)
VALUES('u-bmayor', 'bmayor', 1650000000, 1650001000, 'c-bad-0001');

-- Good: the city's own chunk. Bad: a chunk of a city nobody has.
INSERT INTO CITYCHUNKS(cityguid, cx, cz, timestampclaimed) VALUES
    ('c-bad-0001', 400, 400, 1650000000),
    ('c-missing',  405, 405, 1650000000);

-- Good plot #1, one box. Rows are read in insertion order (rowid).
INSERT INTO PLOTS(guid, name, city, owner, type, price, perms, timestampclaimed, number)
VALUES('p-bad-good', 'Good', 'c-bad-0001', '', 0, 25, 'citizen:use;', 1650000100, 1);
INSERT INTO PLOTBOXES(plotguid, idx, minx, miny, minz, maxx, maxy, maxz) VALUES
    ('p-bad-good', 0, 6401, 60, 6401, 6405, 66, 6405);

-- Bad: plot #2 has no PLOTBOXES row.
INSERT INTO PLOTS(guid, name, city, owner, type, price, perms, timestampclaimed, number)
VALUES('p-bad-nobox', 'NoBox', 'c-bad-0001', '', 0, -1, '', 1650000200, 2);

-- Bad: plots #3 and #4 overlap (6410..6414 on x and z). #3 is read first, #4 is the one that overlaps.
INSERT INTO PLOTS(guid, name, city, owner, type, price, perms, timestampclaimed, number)
VALUES('p-bad-ovl-a', 'OverlapA', 'c-bad-0001', '', 0, -1, '', 1650000300, 3);
INSERT INTO PLOTBOXES(plotguid, idx, minx, miny, minz, maxx, maxy, maxz) VALUES
    ('p-bad-ovl-a', 0, 6408, 60, 6408, 6414, 66, 6414);
INSERT INTO PLOTS(guid, name, city, owner, type, price, perms, timestampclaimed, number)
VALUES('p-bad-ovl-b', 'OverlapB', 'c-bad-0001', '', 0, -1, '', 1650000400, 4);
INSERT INTO PLOTBOXES(plotguid, idx, minx, miny, minz, maxx, maxy, maxz) VALUES
    ('p-bad-ovl-b', 0, 6410, 60, 6410, 6416, 66, 6416);

INSERT INTO CITYLOG(cityguid, ts, event, args) VALUES
    ('c-bad-0001', 1650000000, 'CityCreated', 'bmayor' || char(31) || 'Brokenburg');

INSERT INTO WORLDFLAGS(flag, value) VALUES
    ('pvpew', 0), ('fireew', 0), ('blastew', 0), ('mobspawnew', 0),
    ('pvpfb', 0), ('firefb', 0), ('blastfb', 0), ('mobspawnfb', 0);

PRAGMA user_version = 101;
