﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NBitcoin.Indexer
{
	class IndexerTrace
	{
		static TraceSource _Trace = new TraceSource("NBitcoin.Indexer");
		internal static void ErrorWhileImportingBlockToAzure(uint256 id, Exception ex)
		{
			_Trace.TraceEvent(TraceEventType.Error, 0, "Error while importing " + id + " in azure blob : " + Utils.ExceptionToString(ex));
		}


		internal static void BlockAlreadyUploaded()
		{
			_Trace.TraceInformation("Block already uploaded");
		}

		internal static void BlockUploaded(TimeSpan time, int bytes)
		{
			if(time.TotalSeconds == 0.0)
				time = TimeSpan.FromMilliseconds(10);
			double speed = ((double)bytes / 1024.0) / time.TotalSeconds;
			_Trace.TraceInformation("Block uploaded successfully (" + speed.ToString("0.00") + " KB/S)");
		}

		internal static TraceCorrelation NewCorrelation(string activityName)
		{
			return new TraceCorrelation(_Trace, activityName);
		}

		internal static void StartingImportAt(DiskBlockPos lastPosition)
		{
			_Trace.TraceInformation("Starting import at position " + lastPosition);
		}

		internal static void PositionSaved(DiskBlockPos diskBlockPos)
		{
			_Trace.TraceInformation("New starting import position : " + diskBlockPos.ToString());
		}
	}
}
