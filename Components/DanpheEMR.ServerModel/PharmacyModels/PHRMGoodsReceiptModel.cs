﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DanpheEMR.ServerModel
{
    public class PHRMGoodsReceiptModel
    {
        [Key]
        public int GoodReceiptId { get; set; }
        public int? GoodReceiptPrintId { get; set; }
        public int? PurchaseOrderId { get; set; }
        public string InvoiceNo { get; set; }
        public int? SupplierId { get; set; }
        public DateTime? GoodReceiptDate { get; set; }
        public DateTime? SupplierBillDate { get; set; }
        public decimal? SubTotal { get; set; }
        public decimal? DiscountAmount { get; set; }
        public double? DiscountPercentage { get; set; }
        public decimal? TotalAmount { get; set; }
        public string Remarks { get; set; }
        public decimal? Adjustment { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public virtual List<PHRMGoodsReceiptItemsModel> GoodReceiptItem { get; set; }
        public decimal? VATAmount { get; set; }
        public bool? IsCancel { get; set; }
        public string CancelRemarks { get; set; }
        public int? CancelBy { get; set; }
        public DateTime? CancelOn { get; set; }
        public bool? IsTransferredToACC { get; set; }
        public int StoreId { get; set; }
        public string TransactionType { get; set; }
        public int FiscalYearId { get; set; }
        public bool? IsPacking { get; set; }
        public string PaymentStatus { get; set; }
        public bool? IsItemDiscountApplicable { get; set; }
        [NotMapped]
        public string FiscalYear { get; set; }
        public int? CreditPeriod { get; set; }
        [NotMapped]
        public string StoreName { get; set; }
        [NotMapped]
        public int PatientId { get; set; }
        [NotMapped]
        public string PatientName { get; set; }
        [NotMapped]
        public decimal? SalesAmount { get; set; }
        [NotMapped]
        public List<int> ReferenceIds { get; set; }
        [NotMapped]
        public List<GoodsReceiptModel> PHRMPatientDetail { get; set; }
        [NotMapped]
        public string VoucherName { get; set; }
        [NotMapped]
        public string Type { get; set; }
        [NotMapped]
        public int VoucherId { get; set; }
        [NotMapped]
        public string SupplierName { get; set; }
        [NotMapped]
        public List<SyncBillingAccountingModel> BillSyncs { get; set; }
        [NotMapped]
        public decimal? TradeDiscountAmount { get; set; }
        [NotMapped]
        public decimal? GrVATAmount { get; set; }
        [NotMapped]
        public decimal? GrDiscountAmount { get; set; }
        [NotMapped]
        public decimal? GrCOGSAmount { get; set; }
        [NotMapped]
        public bool? IsGRModified { get; set; }


        // Bikash: 2July'20: Dispensary info added to provide direct stock discptch to Dispensary feature.
        [NotMapped]
        public bool SendDirectToDispensary { get; set; }
        [NotMapped]
        public int SelectedDispensaryId { get; set; }
        public bool IsPaymentDoneFromAcc { get; set; }
    }
}
