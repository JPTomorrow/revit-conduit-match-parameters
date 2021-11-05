using System;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using JPMorrow.Revit.Documents;
using JPMorrow.Revit.Conduit;
using JPMorrow.Tools.Diagnostics;

namespace MainApp
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    [Autodesk.Revit.DB.Macros.AddInId("58F7B2B7-BF6D-4B39-BBF8-13F7D9AAE97E")]
    public partial class ThisApplication : IExternalCommand
    {
        public Result Execute(ExternalCommandData cData, ref string message, ElementSet elements)
        {
            string[] dataDirectories = new string[0];
            bool debugApp = false;

            //set revit documents
            ModelInfo revit_info = ModelInfo.StoreDocuments(cData, dataDirectories, debugApp);

            try
            {
                return ConduitRunMatcher.MatchRuns(revit_info);
            }
            catch (Exception ex)
            {
                debugger.show(header: "ConduitMatchParams", err: ex.Message);
            }

            return Result.Succeeded;
        }

        #region startup
        private void Module_Startup(object sender, EventArgs e)
        {

        }

        private void Module_Shutdown(object sender, EventArgs e)
        {

        }
        #endregion

        #region Revit Macros generated code
        private void InternalStartup()
        {
            this.Startup += new System.EventHandler(Module_Startup);
            this.Shutdown += new System.EventHandler(Module_Shutdown);
        }
        #endregion
    }
}