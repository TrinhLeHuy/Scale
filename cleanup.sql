-- Xóa dữ liệu test: chuyến, QR, phiên cân, audit...
-- Giữ nguyên: sites, devices, workflow_profiles, weighbridges, lanes, gates
TRUNCATE TABLE 
  audit_log_entries,
  gate_commands,
  gate_decisions,
  integration_messages,
  allocations,
  reconciliation_checks,
  reconciliations,
  device_events,
  measurement_sessions,
  quality_samples,
  weigh_tickets,
  weigh_events,
  weigh_sessions,
  checkpoint_events,
  trip_document_lines,
  external_documents,
  qr_tokens,
  approvals,
  incidents,
  trips
CASCADE;

-- Reset sequence
ALTER SEQUENCE trip_no_seq RESTART WITH 1;
