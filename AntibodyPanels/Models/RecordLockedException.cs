using System;

namespace AntibodyPanels.Models
{
    public class RecordLockedException : InvalidOperationException
    {
        public string AccessionNumber { get; }

        public RecordLockedException(string accessionNumber)
            : base($"Specimen {accessionNumber} has a confirmed identification and is locked. " +
                   "Clear the confirmation with a reason before editing reactions, panels, or analysis.")
        {
            AccessionNumber = accessionNumber;
        }
    }
}
