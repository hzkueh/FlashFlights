"""Seed demo flights for MANUAL / live testing of the running stack.

There is no runtime seed-data path in the app until ticket 11 — flights are
otherwise only ever created by tests. So to click through the browse/seat-map
pages (ticket 06) or checkout (ticket 07) against a live `docker compose up`
stack, inject data directly into the two stores.

Two flights' worth of state, three sale states:
  FF100 LHR->BCN  Live      (2 Held, 2 Confirmed, rest Available)  ReferenceFare 229 -> Save 35%
  FF210 SFO->JFK  Upcoming  (all Available)                       no ReferenceFare
  FF330 CDG->FCO  Ended     (6 Confirmed)                         no ReferenceFare

ReferenceFare is the exception, not the rule (CONTEXT.md) — only the Live flight
carries one, so the demo shows both the struck-through "Save X%" badge and the
plain-price flights that have no saving to show.

The two stores by design (ADR-0001, ticket 06):
  - Catalog owns Flight metadata + the advisory SeatCounts projection -> SQLite
    at catalog:/data/catalog.db (list page reads this).
  - Ordering owns Seats + the SeatMovement ledger -> Postgres `ordering-db`
    (seat map reads this live). FlightId is a shared value across both, NOT an FK.

Gotchas this script encodes (both bit me once):
  1. EF Core stores/binds SQLite Guids as UPPER-case TEXT, and SQLite text
     comparison is case-sensitive. The list page reads every row (no WHERE, so
     lowercase ids materialise fine), but the detail page does WHERE Id=@p and
     404s unless the seeded id is upper-cased. -> Flights.Id / FlightSeatCounts
     .FlightId are inserted .upper(); Postgres uuid is case-insensitive so
     Ordering stays lowercase.
  2. Sale state + seat status are computed against TimeProvider.System (real
     wall clock), NOT the CLAUDE.md "today" date. Held holds use a 4h TTL so the
     HoldExpirySweep doesn't Release them mid-demo.
  3. Catalog's SQLite runs in WAL journal mode, so recent writes -- including a
     just-applied EF migration -- live in `catalog.db-wal` until a checkpoint
     folds them into `catalog.db`. A plain `docker compose cp catalog:/data/
     catalog.db` copies ONLY the main file and silently grabs a STALE schema/
     rows while the container is running (this cost a debugging loop when the
     copied db was missing a new column the running service had already added).
     -> Always `docker compose stop catalog` FIRST: a clean shutdown checkpoints
     and removes the -wal, so the cp-out is complete. (If you must copy while it
     runs, also copy catalog.db-wal + catalog.db-shm, or force a checkpoint.)

USAGE (from repo root, stack already up & healthy):
  SP=path/to/this/dir   # a writable working dir
  # Stop catalog BEFORE copying out so WAL is checkpointed into catalog.db (gotcha 3).
  docker compose stop catalog
  docker compose cp catalog:/data/catalog.db "$SP/catalog.db"   # complete live schema
  python seed-demo-data.py "$SP"                                # writes ordering.sql + fills catalog.db
  docker compose exec -T ordering-db psql -U flashflights -d flashflights_ordering \
      -c 'TRUNCATE "SeatMovements","Bookings","Holds","Seats" CASCADE;'
  docker compose exec -T ordering-db psql -U flashflights -d flashflights_ordering -v ON_ERROR_STOP=1 < "$SP/ordering.sql"
  docker compose cp "$SP/catalog.db" catalog:/data/catalog.db && docker compose start catalog

Note: restarting a container can drop Docker Desktop's host port-forward on the
gateway (host curl -> HTTP 000 while the container is healthy internally). Fix:
  docker compose restart gateway
"""
import sqlite3, uuid, datetime as dt, sys, os

SP = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__))
now = dt.datetime.now(dt.timezone.utc).replace(microsecond=0)

def iso_pg(t):   # postgres timestamptz
    return t.isoformat()
def iso_sqlite(t):  # EF Core Microsoft.Data.Sqlite DateTimeOffset text: yyyy-MM-dd HH:mm:ss+HH:MM
    return t.strftime('%Y-%m-%d %H:%M:%S') + '+00:00'

COLS = ['A','B','C','D','E','F']
ROWS = [1,2,3,4]

def make_flight(num, o, d, price, dep, s_start, s_end, ref=None):
    return dict(id=str(uuid.uuid4()), num=num, o=o, d=d, price=price, ref=ref,
                dep=dep, ss=s_start, se=s_end, seats=[], held=set(), confirmed=set())

# A: LIVE — the only flight with a ReferenceFare, so the "Save 35%" badge shows here.
A = make_flight('FF100','LHR','BCN','149.00', now+dt.timedelta(days=2), now-dt.timedelta(hours=1), now+dt.timedelta(hours=3), ref='229.00')
A['held'] = {'1A','1B'}
A['confirmed'] = {'2C','2D'}
# B: UPCOMING
B = make_flight('FF210','SFO','JFK','299.00', now+dt.timedelta(days=5), now+dt.timedelta(hours=2), now+dt.timedelta(hours=5))
# C: ENDED
C = make_flight('FF330','CDG','FCO','89.00', now+dt.timedelta(days=1), now-dt.timedelta(hours=5), now-dt.timedelta(hours=1))
C['confirmed'] = {'1A','1B','1C','2A','2B','3F'}

flights = [A,B,C]

pg = []  # sql lines
def q(v): return "'" + str(v) + "'"

for f in flights:
    total = len(ROWS)*len(COLS)
    held = 0; conf = 0
    for r in ROWS:
        for c in COLS:
            sid = str(uuid.uuid4())
            label = f'{r}{c}'
            f['seats'].append((sid,r,c,label))
            pg.append(f"INSERT INTO \"Seats\"(\"Id\",\"FlightId\",\"RowNumber\",\"ColumnLetter\") VALUES ({q(sid)},{q(f['id'])},{r},{q(c)});")
            if label in f['held']:
                held += 1
                hid = str(uuid.uuid4()); uid=str(uuid.uuid4())
                exp = now+dt.timedelta(hours=4)  # long TTL so the sweep won't Release mid-demo
                pg.append(f"INSERT INTO \"Holds\"(\"Id\",\"FlightId\",\"UserId\",\"CreatedAt\",\"ExpiresAt\",\"PricePerSeat\") VALUES ({q(hid)},{q(f['id'])},{q(uid)},{q(iso_pg(now-dt.timedelta(minutes=1)))},{q(iso_pg(exp))},{f['price']});")
                mid=str(uuid.uuid4())
                pg.append(f"INSERT INTO \"SeatMovements\"(\"Id\",\"SeatId\",\"HoldId\",\"Type\",\"OccurredAt\") VALUES ({q(mid)},{q(sid)},{q(hid)},1,{q(iso_pg(now-dt.timedelta(minutes=1)))});")
            elif label in f['confirmed']:
                conf += 1
                hid = str(uuid.uuid4()); uid=str(uuid.uuid4())
                created = f['ss']+dt.timedelta(minutes=5)
                exp = created+dt.timedelta(minutes=10)
                pg.append(f"INSERT INTO \"Holds\"(\"Id\",\"FlightId\",\"UserId\",\"CreatedAt\",\"ExpiresAt\",\"PricePerSeat\") VALUES ({q(hid)},{q(f['id'])},{q(uid)},{q(iso_pg(created))},{q(iso_pg(exp))},{f['price']});")
                mh=str(uuid.uuid4()); mc=str(uuid.uuid4())
                pg.append(f"INSERT INTO \"SeatMovements\"(\"Id\",\"SeatId\",\"HoldId\",\"Type\",\"OccurredAt\") VALUES ({q(mh)},{q(sid)},{q(hid)},1,{q(iso_pg(created))});")
                pg.append(f"INSERT INTO \"SeatMovements\"(\"Id\",\"SeatId\",\"HoldId\",\"Type\",\"OccurredAt\") VALUES ({q(mc)},{q(sid)},{q(hid)},3,{q(iso_pg(created+dt.timedelta(minutes=1)))});")
    f['total']=total; f['heldc']=held; f['confc']=conf; f['availc']=total-held-conf

with open(os.path.join(SP,'ordering.sql'),'w') as fh:
    fh.write("BEGIN;\n"+"\n".join(pg)+"\nCOMMIT;\n")

# Catalog SQLite (a live schema copy must already exist at $SP/catalog.db)
db = sqlite3.connect(os.path.join(SP,'catalog.db'))
cur = db.cursor()
cur.execute("DELETE FROM FlightSeatCounts"); cur.execute("DELETE FROM Flights")
for f in flights:
    # .upper() is load-bearing — see gotcha 1 in the module docstring.
    # ReferenceFare is nullable: None binds as SQL NULL, and those flights show no saving.
    cur.execute("INSERT INTO Flights(Id,FlightNumber,Origin,Destination,DepartureAt,FlashPrice,ReferenceFare,SaleStartsAt,SaleEndsAt) VALUES (?,?,?,?,?,?,?,?,?)",
        (f['id'].upper(), f['num'], f['o'], f['d'], iso_sqlite(f['dep']), f['price'], f['ref'], iso_sqlite(f['ss']), iso_sqlite(f['se'])))
    cur.execute("INSERT INTO FlightSeatCounts(FlightId,TotalSeats,AvailableSeats,HeldSeats,ConfirmedSeats,LastMovementAt) VALUES (?,?,?,?,?,?)",
        (f['id'].upper(), f['total'], f['availc'], f['heldc'], f['confc'], iso_sqlite(now-dt.timedelta(minutes=1))))
db.commit(); db.close()

print("now(UTC)=", now.isoformat())
for f in flights:
    print(f"{f['num']} {f['o']}->{f['d']} id={f['id']} total={f['total']} avail={f['availc']} held={f['heldc']} conf={f['confc']}")
