using RimWorld;
using Verse;

namespace TraderShips
{
    /// <summary>
    /// Replacement worker for the vanilla OrbitalTraderArrival incident.
    ///
    /// Registered purely through XML (1.7/Patches/OrbitalTrader.xml) by swapping
    /// IncidentDef.workerClass. That works because:
    ///   - IncidentDef.workerClass is late-bound: IncidentDef.cs:148 does
    ///     `(IncidentWorker)Activator.CreateInstance(workerClass)`
    ///   - the two hooks we need are virtual on IncidentWorker:
    ///     CanFireNowSub (IncidentWorker.cs:178), TryExecuteWorker (IncidentWorker.cs:289)
    ///
    /// So a subclass gives exactly the interception the old Harmony prefixes provided,
    /// without making Harmony a dependency of the core mod.
    ///
    /// Behaviour is identical to the removed prefixes:
    ///   disableOrbital == false -> vanilla orbital traders, untouched
    ///   disableOrbital == true  -> delegate to TraderShipsArrival (a landing trade ship)
    ///   ship worker missing     -> fall back to vanilla instead of swallowing the incident
    /// </summary>
    public class IncidentWorkerTraderShipArrival : IncidentWorker_OrbitalTraderArrival
    {
        private static IncidentWorkerTraderShip ShipWorker()
        {
            // Globals.TraderShipsArrival is a [DefOf] field, resolved once defs are loaded.
            IncidentDef def = Globals.TraderShipsArrival;
            return def?.Worker as IncidentWorkerTraderShip;
        }

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            TraderShipsSettings settings = TraderShips.settings;
            if (settings == null || !settings.disableOrbital)
            {
                return base.CanFireNowSub(parms);
            }

            IncidentWorkerTraderShip worker = ShipWorker();
            if (worker == null)
            {
                return base.CanFireNowSub(parms);
            }

            return worker.IsAllowed(parms);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            TraderShipsSettings settings = TraderShips.settings;
            if (settings == null || !settings.disableOrbital)
            {
                return base.TryExecuteWorker(parms);
            }

            IncidentWorkerTraderShip worker = ShipWorker();
            if (worker == null)
            {
                return base.TryExecuteWorker(parms);
            }

            return worker.TryExecuteWorkerPub(parms);
        }
    }
}
