export interface RecentRequest { id: number; propertyTitle: string; submittedUtc: string; status: string }
export interface MaintenanceItem extends RecentRequest {
  propertyAddress: string; description: string; lastUpdatedUtc: string; priority: string;
}
export interface Dashboard {
  tenancies: { propertyTitle: string; propertyAddress: string; startDate: string; endDate?: string }[];
  counts: { open: number; recent: number }; recentMaintenanceRequests: RecentRequest[];
}
export interface Detail {
  request: MaintenanceItem;
  history: { status: string; previousStatus?: string; occurredUtc: string; isLegacyBaseline: boolean; note?: string }[];
}
