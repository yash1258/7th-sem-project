﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using DanpheEMR.Core.Configuration;
using DanpheEMR.ServerModel;
using DanpheEMR.DalLayer;
using System.Data.Entity;
using System.Data.SqlClient;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using DanpheEMR.Utilities;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Http.Features;
using DanpheEMR.CommonTypes;
using DanpheEMR.Core.Caching;
using System.Xml;
using DanpheEMR.Security;
using DanpheEMR.ServerModel.LabModels;
using DanpheEMR.Enums;
using DanpheEMR.Core;
using System.Data;
using System.Transactions;
using System.Net;
using System.IO;
using DanpheEMR.Services;
using System.Web;

namespace DanpheEMR.Controllers
{

    public class LabController : CommonController
    {
        //private bool docPatPortalSync = false;
        private List<LabRunNumberSettingsModel> LabRunNumberSettings = new List<LabRunNumberSettingsModel>();
        private GoogleDriveFileUploadService GoogleDriveFileUpload;
        private string CovidReportFileUploadPath;
        private string CovidReportUrlComonPath;
        public IEmailService _emailService;

        public LabController(IOptions<MyConfiguration> _config, IEmailService emailService) : base(_config)
        {
            //docPatPortalSync = _config.Value.DanphePatientPortalSync;           
            GoogleDriveFileUpload = new GoogleDriveFileUploadService(_config);
            CovidReportFileUploadPath = _config.Value.GoogleDriveFileUpload.UploadFileBasePath;
            CovidReportUrlComonPath = _config.Value.GoogleDriveFileUpload.FileUrlCommon;
            _emailService = emailService;
        }

        // GET: api/values
        [HttpGet]
        public string Get(string reqType,
            int? SampleCode,
            int requisitionId,
            int patientId,
            int labReportId,
            string inputValue,
            int templateId,
            int patientVisitId,
            int employeeId,
            string labTestSpecimen,
            DateTime SampleDate,
            string requisitionIdList,
            string categoryIdList,
            string runNumberType,
            int barCodeNumber,
            string wardName,
            string visitType,
            string formattedSampleCode,
            DateTime FromDate,
            DateTime ToDate,
            DateTime date,
            string search,
            bool? isForLabMaster,
            bool? hasInsurance
            )
        {

            DanpheHTTPResponse<object> responseData = new DanpheHTTPResponse<object>();
            var activeLab = HttpContext.Session.Get<string>("activeLabName");

            try
            {
                LabDbContext labDbContext = new LabDbContext(connString);

                this.LabRunNumberSettings = (List<LabRunNumberSettingsModel>)DanpheCache.GetMasterData(MasterDataEnum.LabRunNumberSettings);


                if (reqType == "testListSummaryByPatientId")
                {
                    List<int> selCategoryList = DanpheJSONConvert.DeserializeObject<List<int>>(categoryIdList);
                    var allEmployee = labDbContext.Employee.ToDictionary(e => e.EmployeeId.ToString(), v => v.FullName);

                    var allreqByPatientId = (from req in labDbContext.Requisitions
                                             join test in labDbContext.LabTests on req.LabTestId equals test.LabTestId
                                             where (req.PatientId == patientId)
                                             && (req.BillingStatus.ToLower() != ENUM_BillingStatus.cancel)
                                             && (req.BillingStatus.ToLower() != ENUM_BillingStatus.returned)
                                             && (selCategoryList.Contains(test.LabTestCategoryId.Value))
                                             && (DbFunctions.TruncateTime(req.CreatedOn) >= FromDate && DbFunctions.TruncateTime(req.CreatedOn) <= ToDate)
                                             select new
                                             {
                                                 test.LabTestCategoryId,
                                                 req.OrderStatus,
                                                 req.OrderDateTime,
                                                 req.CreatedOn,
                                                 req.SampleCodeFormatted,
                                                 req.SampleCreatedBy,
                                                 req.SampleCollectedOnDateTime,
                                                 req.PatientName,
                                                 req.LabTestName,
                                                 req.ResultAddedBy
                                             }).ToList();


                    var requisitions = allreqByPatientId.Where(req => (req.OrderStatus == "active")).ToList();
                    var resultsToAdd = allreqByPatientId.Where(req => (req.OrderStatus == "pending")).ToList();
                    var resultsAdded = allreqByPatientId.Where(req => (req.OrderStatus == "result-added")).ToList();

                    responseData.Results = new
                    {
                        Requisitions = requisitions,
                        ResultsToAdd = resultsToAdd,
                        ResultsAdded = resultsAdded,
                        Employee = allEmployee
                    };
                }
                //it is used in collect sample page
                //sud: 15Sept'18--we're excluding IsActive = false requisitions from Lab_TestRequisitionTable
                else if (reqType == "labRequisition")//!=null not needed for string.
                {
                    var selectedLab = String.IsNullOrEmpty(activeLab) ? "" : activeLab;
                    //FromDate,ToDate
                    //Removed to show all detail regardless of BillingStatus
                    //&& (req.BillingStatus.ToLower() == "paid" || req.BillingStatus.ToLower() == "unpaid" || (req.BillingStatus == "provisional" && req.VisitType == "inpatient"))
                    var histoNdCytoPatients = (from req in labDbContext.Requisitions
                                               join pat in labDbContext.Patients on req.PatientId equals pat.PatientId
                                               where ((req.IsActive.HasValue ? req.IsActive.Value == true : true) && (req.OrderStatus.ToLower() == ENUM_LabOrderStatus.Active) //"active"
                                               && (req.BillingStatus.ToLower() != ENUM_BillingStatus.cancel) // "cancel" ) 
                                               && (req.BillingStatus.ToLower() != ENUM_BillingStatus.returned)// "returned") 
                                               && (req.RunNumberType.ToLower() == ENUM_LabRunNumType.histo || req.RunNumberType.ToLower() == ENUM_LabRunNumType.cyto) // "histo || cyto")
                                               && (req.LabTypeName == selectedLab)
                                               && (DbFunctions.TruncateTime(req.CreatedOn) >= FromDate && DbFunctions.TruncateTime(req.CreatedOn) <= ToDate))
                                               select new
                                               {
                                                   RequisitionId = req.RequisitionId,
                                                   PatientId = req.PatientId,
                                                   PatientName = pat.FirstName + " " + (string.IsNullOrEmpty(pat.MiddleName) ? "" : pat.MiddleName + " ") + pat.LastName,
                                                   PatientCode = pat.PatientCode,
                                                   DateOfBirth = pat.DateOfBirth,
                                                   Gender = pat.Gender,
                                                   PhoneNumber = pat.PhoneNumber,
                                                   LastestRequisitionDate = req.OrderDateTime,
                                                   VisitType = req.VisitType,
                                                   RunNumberType = req.RunNumberType,
                                                   WardName = req.WardName,
                                                   HasInsurance = req.HasInsurance
                                               }).OrderByDescending(a => a.LastestRequisitionDate).ToList();



                    //Removed to show all detail regardless of BillingStatus
                    //&& (req.BillingStatus.ToLower() == "paid" || req.BillingStatus.ToLower() == "unpaid" || (req.BillingStatus == "provisional" && req.VisitType == "inpatient"))
                    var normalPatients = (from req in labDbContext.Requisitions.Include("Patient")
                                          join pat in labDbContext.Patients on req.PatientId equals pat.PatientId
                                          //show only paid and unpaid requisitions in the list.
                                          //show only IsActive=True and IsActive=NULL requests, Hide IsActive=False. -- sud: 15Sept'18
                                          //if IsActive has value then it should be true, if it's null then its true by default. 
                                          where ((req.IsActive.HasValue ? req.IsActive.Value == true : true) && req.OrderStatus.ToLower() == ENUM_LabOrderStatus.Active //"active"
                                          && (req.BillingStatus.ToLower() != ENUM_BillingStatus.cancel)// "cancel") 
                                          && (req.BillingStatus.ToLower() != ENUM_BillingStatus.returned) // "returned")
                                          && (req.RunNumberType.ToLower() == ENUM_LabRunNumType.normal) // "normal")
                                          && (req.LabTypeName == selectedLab)
                                          && (DbFunctions.TruncateTime(req.CreatedOn) >= FromDate && DbFunctions.TruncateTime(req.CreatedOn) <= ToDate))
                                          group req by new { req.Patient, req.VisitType, req.WardName, req.HasInsurance } into p
                                          select new
                                          {
                                              RequisitionId = (long)0,
                                              PatientId = p.Key.Patient.PatientId,
                                              PatientName = p.Key.Patient.FirstName + " " + (string.IsNullOrEmpty(p.Key.Patient.MiddleName) ? "" : p.Key.Patient.MiddleName + " ") + p.Key.Patient.LastName,
                                              PatientCode = p.Key.Patient.PatientCode,
                                              DateOfBirth = p.Key.Patient.DateOfBirth,
                                              Gender = p.Key.Patient.Gender,
                                              PhoneNumber = p.Key.Patient.PhoneNumber,
                                              LastestRequisitionDate = p.Max(r => r.OrderDateTime),
                                              VisitType = p.Key.VisitType,
                                              RunNumberType = "normal",
                                              WardName = p.Key.WardName,
                                              HasInsurance = p.Key.HasInsurance
                                              //IsAdmitted = (from adm in labDbContext.Admissions
                                              //              where adm.PatientId == p.Key.Patient.PatientId && adm.AdmissionStatus == "admitted"
                                              //              select adm.AdmissionStatus).FirstOrDefault() == null ? true : false
                                          }).OrderByDescending(b => b.LastestRequisitionDate).ToList();


                    var combined = histoNdCytoPatients.Union(normalPatients);
                    responseData.Results = combined.OrderByDescending(c => c.LastestRequisitionDate);

                }

                //getting the test of selected patient 
                else if (reqType == "LabSamplesByPatientId")
                {

                    //include patien ---------------------------------
                    var result = (from req in labDbContext.Requisitions.Include("Patient")
                                  join labTest in labDbContext.LabTests on req.LabTestId equals labTest.LabTestId
                                  //show only IsActive=True and IsActive=NULL requests, Hide IsActive=False. -- sud: 15Sept'18
                                  //if IsActive has value then it should be true, if it's null then its true by default. 
                                  where req.PatientId == patientId && (req.IsActive.HasValue ? req.IsActive.Value == true : true) &&
                                  (req.BillingStatus == ENUM_BillingStatus.paid // "paid" 
                                  && req.OrderStatus == ENUM_LabOrderStatus.Active) //"active"
                                  && (req.VisitType.ToLower() == visitType.ToLower())
                                  && (req.RunNumberType.ToLower() == runNumberType.ToLower())

                                  select new PatientLabSampleVM
                                  {
                                      PatientName = req.Patient.FirstName + " " + (string.IsNullOrEmpty(req.Patient.MiddleName) ? "" : req.Patient.MiddleName + " ") + req.Patient.LastName,
                                      OrderStatus = req.OrderStatus,
                                      SpecimenList = labTest.LabTestSpecimen,
                                      RequisitionId = req.RequisitionId,
                                      TestName = req.LabTestName,
                                      SampleCode = req.SampleCode,
                                      OrderDateTime = req.OrderDateTime,
                                      SampleCreatedOn = req.SampleCreatedOn,
                                      ProviderName = req.ProviderName,
                                      PatientId = req.PatientId
                                  }).ToList();
                    if (result.Count != 0)
                    {
                        result.ForEach(res =>
                        {
                            //string specimen = res.Specimen.Split('/')[0];
                            var dateTime = DateTime.Parse(res.OrderDateTime.ToString()).AddHours(-24);

                            if (res.SampleCode == null)
                            {
                                var lastTest = (from labReq in labDbContext.Requisitions
                                                join labTest in labDbContext.LabTests on labReq.LabTestId equals labTest.LabTestId
                                                where labReq.PatientId == patientId
                                                      && res.SpecimenList.Contains(labReq.LabTestSpecimen)
                                                      && labReq.SampleCreatedOn > dateTime
                                                select new
                                                {
                                                    SampleCode = labReq.SampleCode,
                                                    SampleCreatedOn = labReq.SampleCreatedOn,
                                                    SampleCreatedBy = labReq.SampleCreatedBy,
                                                    LabTestSpecimen = labReq.LabTestSpecimen
                                                }).OrderByDescending(a => a.SampleCreatedOn).ThenByDescending(a => a.SampleCode).FirstOrDefault();
                                if (lastTest != null)
                                {
                                    res.LastSampleCode = DateTime.Parse(lastTest.SampleCreatedOn.ToString()).ToString("yyMMdd") + "-" + lastTest.SampleCode;
                                    res.SampleCreatedOn = lastTest.SampleCreatedOn;
                                    res.SampleCreatedBy = lastTest.SampleCreatedBy;
                                    res.LastSpecimenUsed = lastTest.LabTestSpecimen;
                                }
                            }

                        });
                    }

                    responseData.Results = result;
                }

                //getting the test of selected patient 
                else if (reqType == "LabSamplesWithCodeByPatientId")
                {
                    List<PatientLabSampleVM> result = new List<PatientLabSampleVM>();
                    var selectedLab = String.IsNullOrEmpty(activeLab) ? "" : activeLab;
                    bool underInsurance = hasInsurance.HasValue ? hasInsurance.Value : false;
                    //include patien ---------------------------------
                    if (requisitionId == 0)
                    {

                        result = (from req in labDbContext.Requisitions
                                  join pat in labDbContext.Patients on req.PatientId equals pat.PatientId
                                  join labTest in labDbContext.LabTests on req.LabTestId equals labTest.LabTestId
                                  //show only IsActive=True and IsActive=NULL requests, Hide IsActive=False. -- sud: 15Sept'18
                                  //if IsActive has value then it should be true, if it's null then its true by default. 
                                  where req.PatientId == patientId && (req.IsActive.HasValue ? req.IsActive.Value == true : true) &&
                                  (wardName == "null" ? req.WardName == null : req.WardName == wardName) &&
                                  (req.BillingStatus.ToLower() != ENUM_BillingStatus.cancel) // "cancel") 
                                  && (req.BillingStatus.ToLower() != ENUM_BillingStatus.returned)//"returned")
                                  && (req.OrderStatus == ENUM_LabOrderStatus.Active) // "active"
                                  && (req.VisitType.ToLower() == visitType.ToLower())
                                  && (req.RunNumberType.ToLower() == runNumberType.ToLower())
                                  && (req.HasInsurance == underInsurance)
                                  && (req.LabTypeName == selectedLab)
                                  select new PatientLabSampleVM
                                  {
                                      PatientId = req.PatientId,
                                      PatientName = pat.FirstName + " " + (string.IsNullOrEmpty(pat.MiddleName) ? "" : pat.MiddleName + " ") + pat.LastName,
                                      OrderStatus = req.OrderStatus,
                                      SpecimenList = labTest.LabTestSpecimen,
                                      RequisitionId = req.RequisitionId,
                                      TestName = req.LabTestName,
                                      SampleCode = req.SampleCode,
                                      OrderDateTime = req.OrderDateTime,
                                      SampleCreatedOn = req.SampleCreatedOn,
                                      RunNumberType = req.RunNumberType,
                                      ProviderName = req.ProviderName,
                                      HasInsurance = req.HasInsurance//sud:16Jul'19--to show insurance flag in sample collection and other pages.
                                  }).ToList();
                    }
                    else
                    {
                        var reqId = (long)requisitionId;
                        result = (from req in labDbContext.Requisitions
                                  join pat in labDbContext.Patients on req.PatientId equals pat.PatientId
                                  join labTest in labDbContext.LabTests on req.LabTestId equals labTest.LabTestId
                                  //show only IsActive=True and IsActive=NULL requests, Hide IsActive=False. -- sud: 15Sept'18
                                  //if IsActive has value then it should be true, if it's null then its true by default. 
                                  where req.PatientId == patientId && (req.IsActive.HasValue ? req.IsActive.Value == true : true)
                                  && (req.BillingStatus.ToLower() != ENUM_BillingStatus.cancel) // "cancel")
                                  && (req.BillingStatus.ToLower() != ENUM_BillingStatus.returned) //"returned")
                                  && req.OrderStatus == ENUM_LabOrderStatus.Active //"active"
                                  && (req.VisitType.ToLower() == visitType.ToLower())
                                  && (req.RunNumberType.ToLower() == runNumberType.ToLower())
                                  && (req.RequisitionId == reqId)
                                  && (req.LabTypeName == selectedLab)

                                  select new PatientLabSampleVM
                                  {
                                      PatientId = req.PatientId,
                                      PatientName = pat.FirstName + " " + (string.IsNullOrEmpty(pat.MiddleName) ? "" : pat.MiddleName + " ") + pat.LastName,
                                      OrderStatus = req.OrderStatus,
                                      SpecimenList = labTest.LabTestSpecimen,
                                      RequisitionId = req.RequisitionId,
                                      TestName = req.LabTestName,
                                      SampleCode = req.SampleCode,
                                      OrderDateTime = req.OrderDateTime,
                                      SampleCreatedOn = req.SampleCreatedOn,
                                      RunNumberType = req.RunNumberType,
                                      ProviderName = req.ProviderName,
                                      HasInsurance = req.HasInsurance
                                  }).ToList();
                    }


                    if (result.Count != 0)
                    {
                        result.ForEach(res =>
                        {
                            DateTime sampleDate = DateTime.Now;
                        });
                    }

                    responseData.Results = result;
                }

                //getting latest sample code
                else if (reqType == "latest-samplecode")
                {
                    DateTime sampleDate = SampleDate != null ? SampleDate : DateTime.Now;

                    var RunType = runNumberType.ToLower();
                    var VisitType = visitType.ToLower();
                    var PatientId = patientId;
                    bool hasInsuranceFlag = hasInsurance.HasValue ? hasInsurance.Value : false;
                    try
                    {
                        var data = this.GenerateLabSampleCode(labDbContext, RunType, VisitType, PatientId, sampleDate, hasInsuranceFlag);

                        responseData.Results = new
                        {
                            SampleCode = data.SampleCode,
                            SampleNumber = data.SampleNumber,
                            BarCodeNumber = data.BarCodeNumber,
                            SampleLetter = data.SampleLetter,
                            ExistingBarCodeNumbersOfPatient = data.ExistingBarCodeNumbersOfPatient
                        };
                        responseData.Status = "OK";
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }


                }

                else if (reqType == "check-samplecode")
                {
                    SampleDate = (SampleDate != null) ? SampleDate : DateTime.Now;
                    var sampleCode = SampleCode;
                    var RunNumberType = runNumberType.ToLower();
                    var VisitType = visitType.ToLower();
                    var isUnderInsurance = hasInsurance;

                    List<LabRunNumberSettingsModel> allLabRunNumSettings = (List<LabRunNumberSettingsModel>)DanpheCache.GetMasterData(MasterDataEnum.LabRunNumberSettings);


                    //Get the GroupingIndex From visitType and Run Number Type
                    var currentSetting = (from runNumSetting in allLabRunNumSettings
                                          where runNumSetting.VisitType.ToLower() == VisitType
                                          && runNumSetting.RunNumberType.ToLower() == RunNumberType
                                          && runNumSetting.UnderInsurance == isUnderInsurance
                                          select runNumSetting
                                         ).FirstOrDefault();


                    List<SqlParameter> paramList = new List<SqlParameter>() {
                        new SqlParameter("@sampleCode", sampleCode),
                        new SqlParameter("@groupingIndex", currentSetting.RunNumberGroupingIndex),
                        new SqlParameter("@sampleDate", SampleDate)
                    };
                    DataSet dts = DALFunctions.GetDatasetFromStoredProc("SP_LAB_AllRequisitionsBy_SampleCode", paramList, labDbContext);

                    List<LabRequisitionModel> existingRequisition = new List<LabRequisitionModel>();
                    if (dts.Tables.Count > 0)
                    {
                        var strData = JsonConvert.SerializeObject(dts.Tables[0]);
                        existingRequisition = DanpheJSONConvert.DeserializeObject<List<LabRequisitionModel>>(strData);
                    }


                    if ((existingRequisition != null) && (existingRequisition.Count() > 0))
                    {
                        var requisition = existingRequisition[0];
                        responseData.Results = new { Exist = true, PatientName = requisition.PatientName, PatientId = requisition.PatientId, SampleCreatedOn = requisition.SampleCreatedOn };
                    }
                    else
                    {
                        responseData.Results = new { Exist = false };
                    }
                    responseData.Status = "OK";
                }
                else if (reqType == "pendingLabResultsForWorkList")
                {
                    List<int> selCategoryList = DanpheJSONConvert.DeserializeObject<List<int>>(categoryIdList);
                    List<LabPendingResultVM> results = new List<LabPendingResultVM>();
                    var selectedLab = String.IsNullOrEmpty(activeLab) ? "" : activeLab;
                    var reportWithHtmlTemplate = GetAllHTMLLabPendingResults(labDbContext, StartDate: FromDate, EndDate: ToDate, categoryList: selCategoryList, labType: selectedLab, forWorkList: true);

                    var reportWithNormalEntry = GetAllNormalLabPendingResults(labDbContext, StartDate: FromDate, EndDate: ToDate, categoryList: selCategoryList, labType: selectedLab, forWorkList: true);

                    results = reportWithHtmlTemplate.Union(reportWithNormalEntry).ToList();

                    responseData.Results = results.OrderBy(d => d.BarCodeNumber).ThenBy(c => c.SampleCode).ToList();
                }
                //getting the data from requisition and component to add 
                //in the labresult service ....(used in collect-sample.component)
                else if (reqType == "pendingLabResults")
                {
                    List<int> selCategoryList = DanpheJSONConvert.DeserializeObject<List<int>>(categoryIdList);
                    List<LabPendingResultVM> results = new List<LabPendingResultVM>();
                    var selectedLab = String.IsNullOrEmpty(activeLab) ? "" : activeLab;
                    var dVendorId = (from vendor in labDbContext.LabVendors
                                     where vendor.IsDefault == true
                                     select vendor.LabVendorId).FirstOrDefault();
                    var reportWithHtmlTemplate = GetAllHTMLLabPendingResults(labDbContext, StartDate: FromDate, EndDate: ToDate, categoryList: selCategoryList, labType: selectedLab, defaultVendorId: dVendorId);

                    var reportWithNormalEntry = GetAllNormalLabPendingResults(labDbContext, StartDate: FromDate, EndDate: ToDate, categoryList: selCategoryList, labType: selectedLab, defaultVendorId: dVendorId);

                    results = reportWithHtmlTemplate.Union(reportWithNormalEntry).ToList();

                    //foreach (var rep in reportWithHtmlTemplate)
                    //{
                    //    rep.SampleCodeFormatted = GetSampleCodeFormatted(rep.SampleCode, rep.SampleDate ?? default(DateTime), rep.VisitType.ToLower(), rep.RunNumType.ToLower());
                    //    results.Add(rep);
                    //}
                    //foreach (var repNormal in reportWithNormalEntry)
                    //{
                    //    repNormal.SampleCodeFormatted = GetSampleCodeFormatted(repNormal.SampleCode, repNormal.SampleDate ?? default(DateTime), repNormal.VisitType, repNormal.RunNumType);
                    //    results.Add(repNormal);
                    //}

                    responseData.Results = results.OrderBy(d => d.BarCodeNumber).ToList();
                }

                else if (reqType == "pending-reports")
                {
                    List<int> selCategoryList = DanpheJSONConvert.DeserializeObject<List<int>>(categoryIdList);
                    List<LabPendingResultVM> results = new List<LabPendingResultVM>();
                    var selectedLab = String.IsNullOrEmpty(activeLab) ? "" : activeLab;

                    var pendingNormalReports = GetAllNormalLabPendingReports(labDbContext, StartDate: FromDate, EndDate: ToDate, categoryList: selCategoryList, labType: selectedLab);
                    var pendingHtmlNCS = GetAllHTMLLabPendingReports(labDbContext, StartDate: FromDate, EndDate: ToDate, categoryList: selCategoryList, labType: selectedLab);

                    results = pendingHtmlNCS.Union(pendingNormalReports).ToList();

                    responseData.Results = results.OrderBy(d => d.ResultAddedOn);

                }
                else if (reqType == "final-reports")
                {
                    CoreDbContext coreDbContext = new CoreDbContext(connString);
                    search = string.IsNullOrEmpty(search) ? string.Empty : search.Trim().ToLower();
                    var selectedLab = String.IsNullOrEmpty(activeLab) ? "" : activeLab;
                    bool isForLabMasterPage = isForLabMaster ?? false;

                    List<int> selCategoryList = DanpheJSONConvert.DeserializeObject<List<int>>(categoryIdList);

                    var allFinalReports = GetAllLabFinalReportsFromSP(labDbContext, StartDate: FromDate, EndDate: ToDate, categoryList: selCategoryList, labType: selectedLab, isForLabMaster: isForLabMasterPage);
                    var finalReports = GetFinalReportListFormatted(allFinalReports);

                    if (!string.IsNullOrEmpty(search))
                    {
                        finalReports = finalReports.Where(r => (r.BarCodeNumber.ToString() + " " + r.PatientName + " " + r.PatientCode + " " + r.SampleCode.ToString() + " " + r.PhoneNumber + " " + r.SampleCodeFormatted).ToLower().Contains(search))
                                                   .OrderByDescending(rep => rep.SampleDate).ThenByDescending(x => x.SampleCode).ThenByDescending(a => a.PatientId).ToList();
                    }
                    else
                    {
                        finalReports = finalReports.OrderBy(rep => rep.ReportId).ToList();
                    }


                    // 14th Jan 2020: Here we are filtering data as per search text, this will avoid maximum records
                    // but not improve performance same as other server side search feature pages.
                    // Because other pages filter db data, here we are filtering with results which is get into finalReports.
                    // we need to apply search on above function => GetAllLabProvisionalFinalReports, GetAllLabPaidUnpaidFinalReports
                    if (CommonFunctions.GetCoreParameterBoolValue(coreDbContext, "Common", "ServerSideSearchComponent", "LaboratoryFinalReports") == true && search == "")
                    {
                        finalReports = finalReports.AsEnumerable().Take(CommonFunctions.GetCoreParameterIntValue(coreDbContext, "Common", "ServerSideSearchListLength"));
                    }
                    var reportFormattedForFinalReportPage = GetFinalReportListFormattedInFinalReportPage(finalReports);
                    responseData.Results = reportFormattedForFinalReportPage;
                }
                else if (reqType == "reportsByPatIdInReportDispatch")
                {
                    bool isForLabMasterPage = true;

                    List<int> selCategoryList = DanpheJSONConvert.DeserializeObject<List<int>>(categoryIdList);

                    var allFinalReports = GetAllLabFinalReportsFromSP(labDbContext, StartDate: FromDate, EndDate: ToDate, categoryList: selCategoryList, isForLabMaster: isForLabMasterPage, PatientId: patientId);
                    var finalReports = GetFinalReportListFormatted(allFinalReports);

                    responseData.Results = finalReports.OrderBy(rep => rep.ReportId).ToList();
                }
                else if (reqType == "patientListForReportDispatch")
                {
                    List<string> selCategoryList = DanpheJSONConvert.DeserializeObject<List<string>>(categoryIdList);
                    List<SqlParameter> paramList = new List<SqlParameter>() {
                            new SqlParameter("@StartDate", FromDate),
                            new SqlParameter("@EndDate", ToDate),
                            new SqlParameter("@CategoryList", String.Join(",",selCategoryList))
                    };

                    DataTable dtbl = DALFunctions.GetDataTableFromStoredProc("SP_LAB_GetPatientListForReportDispatch", paramList, labDbContext);
                    responseData.Results = dtbl;
                }
                else if (reqType == "final-report-patientlist")
                {
                    var selectedLab = String.IsNullOrEmpty(activeLab) ? "" : activeLab;
                    List<string> selCategoryList = DanpheJSONConvert.DeserializeObject<List<string>>(categoryIdList);
                    List<SqlParameter> paramList = new List<SqlParameter>() {
                            new SqlParameter("@FromDate", FromDate),
                            new SqlParameter("@ToDate", ToDate),
                            new SqlParameter("@LabTypeName", selectedLab),
                            new SqlParameter("@CategoryIdCsv", String.Join(",",selCategoryList))
                    };

                    DataTable dtbl = DALFunctions.GetDataTableFromStoredProc("SP_LAB_GetPatAndReportInfoForFinalReport", paramList, labDbContext);
                    responseData.Results = dtbl;
                }

                else if (reqType == "filtered-patient-list")
                {
                    List<int> selCategoryList = DanpheJSONConvert.DeserializeObject<List<int>>(categoryIdList);
                    var patientList = (from req in labDbContext.Requisitions
                                       join test in labDbContext.LabTests on req.LabTestId equals test.LabTestId
                                       where (DbFunctions.TruncateTime(req.CreatedOn) >= FromDate && DbFunctions.TruncateTime(req.CreatedOn) <= ToDate)
                                       && selCategoryList.Contains(test.LabTestCategoryId.Value)
                                       group new { req }
                                       by new
                                       {
                                           req.PatientId,
                                           req.Patient,
                                           req.WardName
                                       } into grp
                                       select new
                                       {
                                           grp.Key.PatientId,
                                           grp.Key.Patient.PatientCode,
                                           grp.Key.Patient.ShortName,
                                           grp.Key.Patient.Gender,
                                           grp.Key.Patient.DateOfBirth,
                                           grp.Key.WardName,
                                           IsSelected = false
                                       }).ToList();
                    responseData.Results = patientList;
                }

                else if (reqType == "allLabDataFromBarCodeNumber")
                {
                    LabMasterModel LabMasterData = new LabMasterModel();

                    int BarCodeNumber = barCodeNumber;

                    var firstReq = (from reqsn in labDbContext.Requisitions
                                    join patient in labDbContext.Patients on reqsn.PatientId equals patient.PatientId
                                    where reqsn.BarCodeNumber == BarCodeNumber
                                    select new
                                    {
                                        PatientId = patient.PatientId,
                                        Gender = patient.Gender,
                                        PatientCode = patient.PatientCode,
                                        PatientDob = patient.DateOfBirth,
                                        FirstName = patient.FirstName,
                                        MiddleName = patient.MiddleName,
                                        LastName = patient.LastName,
                                        SampleCreatedOn = reqsn.SampleCreatedOn
                                    }).FirstOrDefault();

                    LabMasterData.PatientId = firstReq.PatientId;
                    LabMasterData.PatientName = firstReq.FirstName + " " + (string.IsNullOrEmpty(firstReq.MiddleName) ? "" : firstReq.MiddleName + " ") + firstReq.LastName;
                    LabMasterData.Gender = firstReq.Gender;
                    LabMasterData.PatientCode = firstReq.PatientCode;
                    LabMasterData.DateOfBirth = firstReq.PatientDob;
                    LabMasterData.BarCodeNumber = BarCodeNumber;
                    LabMasterData.SampleCollectedOn = firstReq.SampleCreatedOn;

                    //Anish 24 Nov: This function below has single function for both getting AllLabData from BarcodeNumber or RunNumber-
                    //but is slow due large number of for loop containing if-else statement inside it
                    //LabMasterData = AllLabDataFromRunNumOrBarCode(labDbContext, BarCodeNumber);


                    //All PendingLabResults (for Add-Result page) of Particular Barcode Number
                    var reportWithHtmlTemplate = GetAllHTMLLabPendingResults(labDbContext, BarcodeNumber: BarCodeNumber);

                    var reportWithNormalEntry = GetAllNormalLabPendingResults(labDbContext, BarcodeNumber: BarCodeNumber);


                    foreach (var rep in reportWithHtmlTemplate)
                    {
                        rep.SampleCodeFormatted = GetSampleCodeFormatted(rep.SampleCode, rep.SampleDate ?? default(DateTime), rep.VisitType, rep.RunNumType);
                        LabMasterData.AddResult.Add(rep);
                    }
                    foreach (var repNormal in reportWithNormalEntry)
                    {
                        repNormal.SampleCodeFormatted = GetSampleCodeFormatted(repNormal.SampleCode, repNormal.SampleDate ?? default(DateTime), repNormal.VisitType, repNormal.RunNumType);
                        LabMasterData.AddResult.Add(repNormal);
                    }

                    LabMasterData.AddResult = LabMasterData.AddResult.OrderByDescending(d => d.SampleDate).ThenByDescending(c => c.SampleCode).ToList();


                    var pendingNormalReports = GetAllNormalLabPendingReports(labDbContext, BarcodeNumber: BarCodeNumber);
                    var pendingHtmlNCS = GetAllHTMLLabPendingReports(labDbContext, BarcodeNumber: BarCodeNumber);

                    var pendingRep = pendingHtmlNCS.Union(pendingNormalReports);
                    LabMasterData.PendingReport = pendingRep.OrderByDescending(rep => rep.ResultAddedOn).ThenByDescending(x => x.SampleDate).ThenByDescending(a => a.SampleCode).ToList();


                    var allBillingStatusFinalReports = GetAllLabFinalReportsFromSP(labDbContext, BarcodeNumber: BarCodeNumber);
                    var finalReports = GetFinalReportListFormatted(allBillingStatusFinalReports);
                    finalReports = finalReports.OrderByDescending(rep => rep.SampleDate).ThenByDescending(x => x.SampleCode).ThenByDescending(a => a.PatientId).ToList();
                    LabMasterData.FinalReport = finalReports.ToList();

                    //var parameterOutPatWithProvisional = (from coreData in labDbContext.AdminParameters
                    //                                      where coreData.ParameterGroupName.ToLower() == "lab"
                    //                                      && coreData.ParameterName == "AllowLabReportToPrintOnProvisional"
                    //                                      select coreData.ParameterValue).FirstOrDefault();

                    //bool allowOutPatWithProv = false;

                    //if (!String.IsNullOrEmpty(parameterOutPatWithProvisional) && parameterOutPatWithProvisional.ToLower() == "true")
                    //{
                    //    allowOutPatWithProv = true;
                    //}


                    //foreach (var rep in LabMasterData.FinalReport)
                    //{
                    //    if (!String.IsNullOrEmpty(rep.VisitType) && !String.IsNullOrEmpty(rep.BillingStatus))
                    //    {
                    //        rep.IsValidToPrint = ValidatePrintOption(allowOutPatWithProv, rep.VisitType, rep.BillingStatus);
                    //    }
                    //    foreach (var test in rep.Tests)
                    //    {
                    //        if (!String.IsNullOrEmpty(rep.VisitType) && !String.IsNullOrEmpty(test.BillingStatus))
                    //        {
                    //            test.ValidTestToPrint = ValidatePrintOption(allowOutPatWithProv, rep.VisitType, test.BillingStatus);
                    //        }
                    //    }
                    //}

                    responseData.Results = LabMasterData;

                }

                else if (reqType == "allLabDataFromRunNumber")
                {
                    LabMasterModel LabMasterData = new LabMasterModel();
                    string completeSampleCode = formattedSampleCode;

                    //LabMasterData = AllLabDataFromRunNumOrBarCode(labDbContext, formattedCode:completeSampleCode);


                    List<LabRunNumberSettingsModel> allLabRunNumberSettings = (List<LabRunNumberSettingsModel>)DanpheCache.GetMasterData(MasterDataEnum.LabRunNumberSettings);

                    //assuming all the settings have same separator
                    var separator = allLabRunNumberSettings[0].FormatSeparator;
                    var mainCode = formattedSampleCode.Split(separator[0]);
                    int samplNumber = Convert.ToInt32(mainCode[0]);
                    int code = Convert.ToInt32(mainCode[1]);

                    if (allLabRunNumberSettings[0].FormatInitialPart != "num")
                    {
                        samplNumber = code;
                        code = Convert.ToInt32(mainCode[0]);
                    }


                    DateTime englishDateToday = DateTime.Now;
                    NepaliDateType nepaliDate = DanpheDateConvertor.ConvertEngToNepDate(englishDateToday);

                    if (code > 32)
                    {
                        nepaliDate.Year = 2000 + code;
                    }
                    else
                    {
                        nepaliDate.Day = code;
                    }

                    englishDateToday = DanpheDateConvertor.ConvertNepToEngDate(nepaliDate);



                    var reportWithHtmlTemplate = GetAllHTMLLabPendingResults(labDbContext, SampleNumber: samplNumber, SampleCode: code, EnglishDateToday: englishDateToday);

                    var reportWithNormalEntry = GetAllNormalLabPendingResults(labDbContext, SampleNumber: samplNumber, SampleCode: code, EnglishDateToday: englishDateToday);



                    foreach (var rep in reportWithHtmlTemplate)
                    {
                        var letter = allLabRunNumberSettings.Where(t => t.VisitType == rep.VisitType.ToLower() && t.RunNumberType == rep.RunNumType.ToLower()).Select(s => s.StartingLetter).FirstOrDefault();
                        if (!String.IsNullOrEmpty(letter))
                        {
                            completeSampleCode = letter + formattedSampleCode;
                        }

                        if (rep.SampleCodeFormatted == completeSampleCode)
                        {
                            LabMasterData.AddResult.Add(rep);
                        }

                    }
                    foreach (var repNormal in reportWithNormalEntry)
                    {
                        var letter = allLabRunNumberSettings.Where(t => t.VisitType == repNormal.VisitType.ToLower() && t.RunNumberType == repNormal.RunNumType.ToLower()).Select(s => s.StartingLetter).FirstOrDefault();
                        if (!String.IsNullOrEmpty(letter))
                        {
                            completeSampleCode = letter + formattedSampleCode;
                        }
                        if (repNormal.SampleCodeFormatted == completeSampleCode)
                        {
                            LabMasterData.AddResult.Add(repNormal);
                        }
                    }

                    LabMasterData.AddResult = LabMasterData.AddResult.OrderByDescending(d => d.SampleDate).ThenByDescending(c => c.SampleCode).ToList();
                    var pendingNormalReports = GetAllNormalLabPendingReports(labDbContext, SampleNumber: samplNumber, SampleCode: code, EnglishDateToday: englishDateToday);
                    var pendingHtmlNCS = GetAllHTMLLabPendingReports(labDbContext, SampleNumber: samplNumber, SampleCode: code, EnglishDateToday: englishDateToday);

                    foreach (var rep in pendingHtmlNCS)
                    {
                        var letter = allLabRunNumberSettings.Where(t => t.VisitType == rep.VisitType.ToLower() && t.RunNumberType == rep.RunNumType.ToLower()).Select(s => s.StartingLetter).FirstOrDefault();
                        if (!String.IsNullOrEmpty(letter))
                        {
                            completeSampleCode = letter + formattedSampleCode;
                        }
                        if (rep.SampleCodeFormatted == completeSampleCode)
                        {
                            LabMasterData.PendingReport.Add(rep);
                        }
                    }
                    foreach (var repNormal in pendingNormalReports)
                    {
                        var letter = allLabRunNumberSettings.Where(t => t.VisitType == repNormal.VisitType.ToLower() && t.RunNumberType == repNormal.RunNumType.ToLower()).Select(s => s.StartingLetter).FirstOrDefault();
                        if (!String.IsNullOrEmpty(letter))
                        {
                            completeSampleCode = letter + formattedSampleCode;
                        }
                        if (repNormal.SampleCodeFormatted == completeSampleCode)
                        {
                            LabMasterData.PendingReport.Add(repNormal);
                        }
                    }

                    var allBillingStatusFinalReports = GetAllLabFinalReportsFromSP(labDbContext, SampleNumber: samplNumber, SampleCode: code, EnglishDateToday: englishDateToday);
                    var finalReports = GetFinalReportListFormatted(allBillingStatusFinalReports);
                    finalReports = finalReports.OrderByDescending(rep => rep.SampleDate).ThenByDescending(x => x.SampleCode).ThenByDescending(a => a.PatientId).ToList();
                    LabMasterData.FinalReport = finalReports.OrderByDescending(rep => rep.SampleDate).ThenByDescending(x => x.SampleCode).ThenByDescending(a => a.PatientId).ToList();

                    //var parameterOutPatWithProvisional = (from coreData in labDbContext.AdminParameters
                    //                                      where coreData.ParameterGroupName.ToLower() == "lab"
                    //                                      && coreData.ParameterName == "AllowLabReportToPrintOnProvisional"
                    //                                      select coreData.ParameterValue).FirstOrDefault();

                    //bool allowOutPatWithProv = false;

                    //if (!String.IsNullOrEmpty(parameterOutPatWithProvisional) && parameterOutPatWithProvisional.ToLower() == "true")
                    //{
                    //    allowOutPatWithProv = true;
                    //}


                    //foreach (var rep in finalReports)
                    //{
                    //    var letter = allLabRunNumberSettings.Where(t => t.VisitType == rep.VisitType.ToLower() && t.RunNumberType == rep.RunNumType.ToLower()).Select(s => s.StartingLetter).FirstOrDefault();
                    //    if (!String.IsNullOrEmpty(letter))
                    //    {
                    //        completeSampleCode = letter + formattedSampleCode;
                    //    }
                    //    if (!String.IsNullOrEmpty(rep.VisitType) && !String.IsNullOrEmpty(rep.BillingStatus))
                    //    {
                    //        rep.IsValidToPrint = ValidatePrintOption(allowOutPatWithProv, rep.VisitType, rep.BillingStatus);
                    //    }

                    //    foreach (var test in rep.Tests)
                    //    {
                    //        if (!String.IsNullOrEmpty(rep.VisitType) && !String.IsNullOrEmpty(test.BillingStatus))
                    //        {
                    //            test.ValidTestToPrint = ValidatePrintOption(allowOutPatWithProv, rep.VisitType, test.BillingStatus);
                    //        }
                    //    }

                    //    if (rep.SampleCodeFormatted == completeSampleCode)
                    //    {
                    //        LabMasterData.FinalReport.Add(rep);
                    //    }
                    //}
                    responseData.Results = LabMasterData;
                }

                else if (reqType == "allLabDataFromPatientName")
                {
                    LabMasterModel LabMasterData = new LabMasterModel();

                    int patId = patientId;

                    //All LabRequisitions of Patient
                    var histoPatients = (from req in labDbContext.Requisitions.Include("Patient")
                                         join pat in labDbContext.Patients on req.PatientId equals pat.PatientId
                                         where ((req.IsActive.HasValue ? req.IsActive.Value == true : true) && req.OrderStatus.ToLower() == ENUM_LabOrderStatus.Active// "active"
                                         && req.PatientId == patId
                                         && (req.BillingStatus.ToLower() != ENUM_BillingStatus.cancel) //"cancel") 
                                         && (req.BillingStatus.ToLower() != ENUM_BillingStatus.returned) //"returned") 
                                         && req.RunNumberType.ToLower() == ENUM_LabRunNumType.histo) // "histo")
                                         select new Requisition
                                         {
                                             RequisitionId = req.RequisitionId,
                                             PatientId = req.PatientId,
                                             PatientName = req.Patient.FirstName + " " + (string.IsNullOrEmpty(req.Patient.MiddleName) ? "" : req.Patient.MiddleName + " ") + req.Patient.LastName,
                                             PatientCode = req.Patient.PatientCode,
                                             DateOfBirth = req.Patient.DateOfBirth,
                                             Gender = req.Patient.Gender,
                                             PhoneNumber = req.Patient.PhoneNumber,
                                             LastestRequisitionDate = req.OrderDateTime,
                                             VisitType = req.VisitType,
                                             RunNumberType = req.RunNumberType,
                                             WardName = req.WardName
                                         }).OrderByDescending(a => a.LastestRequisitionDate).ToList(); ;

                    //Removed to show all detail regardless of BillingStatus
                    //&& (req.BillingStatus.ToLower() == "paid" || req.BillingStatus.ToLower() == "unpaid" || (req.BillingStatus == "provisional" && req.VisitType == "inpatient"))
                    var cytoPatients = (from req in labDbContext.Requisitions.Include("Patient")
                                        join pat in labDbContext.Patients on req.PatientId equals pat.PatientId
                                        where ((req.IsActive.HasValue ? req.IsActive.Value == true : true) && req.OrderStatus.ToLower() == ENUM_LabOrderStatus.Active //"active"
                                        && req.PatientId == patId
                                        && (req.BillingStatus.ToLower() != ENUM_BillingStatus.cancel) // "cancel") 
                                        && (req.BillingStatus.ToLower() != ENUM_BillingStatus.returned) // "returned")
                                        && req.RunNumberType.ToLower() == ENUM_LabRunNumType.cyto) // // "cyto")
                                        select new Requisition
                                        {
                                            RequisitionId = req.RequisitionId,
                                            PatientId = req.PatientId,
                                            PatientName = req.Patient.FirstName + " " + (string.IsNullOrEmpty(req.Patient.MiddleName) ? "" : req.Patient.MiddleName + " ") + req.Patient.LastName,
                                            PatientCode = req.Patient.PatientCode,
                                            DateOfBirth = req.Patient.DateOfBirth,
                                            Gender = req.Patient.Gender,
                                            PhoneNumber = req.Patient.PhoneNumber,
                                            LastestRequisitionDate = req.OrderDateTime,
                                            VisitType = req.VisitType,
                                            RunNumberType = req.RunNumberType,
                                            WardName = req.WardName
                                        }).OrderByDescending(a => a.LastestRequisitionDate).ToList();

                    //.OrderByDescending(a => a.LatestRequisitionDate).ToList()

                    //Removed to show all detail regardless of BillingStatus
                    //&& (req.BillingStatus.ToLower() == "paid" || req.BillingStatus.ToLower() == "unpaid" || (req.BillingStatus == "provisional" && req.VisitType == "inpatient"))
                    var normalPatients = (from req in labDbContext.Requisitions.Include("Patient")
                                          join pat in labDbContext.Patients on req.PatientId equals pat.PatientId
                                          //show only paid and unpaid requisitions in the list.
                                          //show only IsActive=True and IsActive=NULL requests, Hide IsActive=False. -- sud: 15Sept'18
                                          //if IsActive has value then it should be true, if it's null then its true by default. 
                                          where ((req.IsActive.HasValue ? req.IsActive.Value == true : true) && req.OrderStatus.ToLower() == ENUM_LabOrderStatus.Active //"active"
                                          && req.PatientId == patId
                                          && (req.BillingStatus.ToLower() != ENUM_BillingStatus.cancel) // "cancel")
                                          && (req.BillingStatus.ToLower() != ENUM_BillingStatus.returned) //"returned") 
                                          && req.RunNumberType.ToLower() == ENUM_LabRunNumType.normal) // "normal")
                                          group req by new { req.Patient, req.VisitType, req.WardName } into p
                                          select new Requisition
                                          {
                                              RequisitionId = (long)0,
                                              PatientId = p.Key.Patient.PatientId,
                                              PatientName = p.Key.Patient.FirstName + " " + (string.IsNullOrEmpty(p.Key.Patient.MiddleName) ? "" : p.Key.Patient.MiddleName + " ") + p.Key.Patient.LastName,
                                              PatientCode = p.Key.Patient.PatientCode,
                                              DateOfBirth = p.Key.Patient.DateOfBirth,
                                              Gender = p.Key.Patient.Gender,
                                              PhoneNumber = p.Key.Patient.PhoneNumber,
                                              LastestRequisitionDate = p.Max(r => r.OrderDateTime),
                                              VisitType = p.Key.VisitType,
                                              RunNumberType = "normal",
                                              WardName = p.Key.WardName
                                              //IsAdmitted = (from adm in labDbContext.Admissions
                                              //              where adm.PatientId == p.Key.Patient.PatientId && adm.AdmissionStatus == "admitted"
                                              //              select adm.AdmissionStatus).FirstOrDefault() == null ? true : false
                                          }).OrderByDescending(b => b.LastestRequisitionDate).ToList();


                    var combined = histoPatients.Union(cytoPatients).Union(normalPatients);
                    var allReqs = combined.OrderByDescending(c => c.LastestRequisitionDate);
                    List<Requisition> allRequisitionsOfPat = new List<Requisition>(allReqs);
                    LabMasterData.LabRequisitions = allRequisitionsOfPat;



                    //All PendingLabResults (for Add-Result page) of Particular Barcode Number
                    var reportWithHtmlTemplate = GetAllHTMLLabPendingResults(labDbContext, PatientId: patId);
                    var reportWithNormalEntry = GetAllNormalLabPendingResults(labDbContext, PatientId: patId);

                    foreach (var rep in reportWithHtmlTemplate)
                    {
                        LabMasterData.AddResult.Add(rep);
                    }
                    foreach (var repNormal in reportWithNormalEntry)
                    {
                        LabMasterData.AddResult.Add(repNormal);
                    }

                    LabMasterData.AddResult = LabMasterData.AddResult.OrderByDescending(d => d.SampleDate).ThenByDescending(c => c.SampleCode).ToList();


                    var pendingNormalReports = GetAllNormalLabPendingReports(labDbContext, PatientId: patId);
                    var pendingHtmlNCS = GetAllHTMLLabPendingReports(labDbContext, PatientId: patId);
                    var pendingRep = pendingHtmlNCS.Union(pendingNormalReports);
                    LabMasterData.PendingReport = pendingRep.OrderByDescending(rep => rep.ResultAddedOn).ThenByDescending(x => x.SampleDate).ThenByDescending(a => a.SampleCode).ToList();


                    var allBillingStatusFinalReports = GetAllLabFinalReportsFromSP(labDbContext, PatientId: patId);
                    var finalReports = GetFinalReportListFormatted(allBillingStatusFinalReports);
                    finalReports = finalReports.OrderByDescending(rep => rep.SampleDate).ThenByDescending(x => x.SampleCode).ThenByDescending(a => a.PatientId).ToList();
                    LabMasterData.FinalReport = finalReports.OrderByDescending(rep => rep.SampleDate).ThenByDescending(x => x.SampleCode).ThenByDescending(a => a.PatientId).ToList();


                    //var finalReportsProv = GetAllLabProvisionalFinalReports(labDbContext, PatientId: patId);
                    //var finalReportsPaidUnpaid = GetAllLabPaidUnpaidFinalReports(labDbContext, PatientId: patId);


                    //var finalReports = finalReportsProv.Union(finalReportsPaidUnpaid);
                    //finalReports = finalReports.OrderByDescending(rep => rep.SampleDate).ThenByDescending(x => x.SampleCode).ThenByDescending(a => a.PatientId).ToList();
                    //List<LabPendingResultVM> finalReportList = new List<LabPendingResultVM>(finalReports);

                    //LabMasterData.FinalReport = finalReportList;


                    //var parameterOutPatWithProvisional = (from coreData in labDbContext.AdminParameters
                    //                                      where coreData.ParameterGroupName.ToLower() == "lab"
                    //                                      && coreData.ParameterName == "AllowLabReportToPrintOnProvisional"
                    //                                      select coreData.ParameterValue).FirstOrDefault();

                    //bool allowOutPatWithProv = false;

                    //if (!String.IsNullOrEmpty(parameterOutPatWithProvisional) && parameterOutPatWithProvisional.ToLower() == "true")
                    //{
                    //    allowOutPatWithProv = true;
                    //}


                    //foreach (var rep in LabMasterData.FinalReport)
                    //{
                    //    if (!String.IsNullOrEmpty(rep.VisitType) && !String.IsNullOrEmpty(rep.BillingStatus))
                    //    {
                    //        rep.IsValidToPrint = ValidatePrintOption(allowOutPatWithProv, rep.VisitType, rep.BillingStatus);
                    //    }
                    //    foreach (var test in rep.Tests)
                    //    {
                    //        if (!String.IsNullOrEmpty(rep.VisitType) && !String.IsNullOrEmpty(test.BillingStatus))
                    //        {
                    //            test.ValidTestToPrint = ValidatePrintOption(allowOutPatWithProv, rep.VisitType, test.BillingStatus);
                    //        }
                    //    }
                    //}

                    responseData.Results = LabMasterData;

                }

                else if (reqType == "labReportFromReqIdList")
                {
                    List<Int64> reqIdList = DanpheJSONConvert.DeserializeObject<List<Int64>>(requisitionIdList);
                    var allBarCode = (from requisition in labDbContext.Requisitions
                                      where reqIdList.Contains(requisition.RequisitionId)
                                      select requisition.BarCodeNumber).Distinct().ToList();


                    if (allBarCode != null && allBarCode.Count == 1)
                    {
                        LabReportVM labReport = DanpheEMR.Labs.LabsBL.GetLabReportVMForReqIds(labDbContext, reqIdList, CovidReportUrlComonPath);
                        //labReport.Lookups.SampleCodeFormatted = GetSampleCodeFormatted(labReport.Lookups.SampleCode, labReport.Lookups.SampleDate ?? default(DateTime), labReport.Lookups.VisitType, labReport.Lookups.RunNumberType);

                        labReport.ValidToPrint = true;
                        labReport.BarCodeNumber = allBarCode[0];
                        responseData.Results = labReport;
                    }
                    else
                    {
                        responseData.Status = "Failed";
                        responseData.ErrorMessage = "Multiple Barcode found for List of RequisitionID";
                    }

                }
                //Get the data for Report dispatch from Multiple requisition list of Req Id
                else if (reqType == "labReportFromListOfReqIdList")
                {
                    try
                    {
                        List<List<Int64>> reqIdList = DanpheJSONConvert.DeserializeObject<List<List<Int64>>>(requisitionIdList);
                        List<LabReportVM> multipleReports = new List<LabReportVM>();
                        foreach (var reqList in reqIdList)
                        {
                            if (reqList.Count > 0)
                            {
                                var allBarCode = (from requisition in labDbContext.Requisitions
                                                  where reqList.Contains(requisition.RequisitionId)
                                                  select requisition.BarCodeNumber).Distinct().ToList();
                                if (allBarCode != null && allBarCode.Count == 1)
                                {
                                    LabReportVM labReport = DanpheEMR.Labs.LabsBL.GetLabReportVMForReqIds(labDbContext, reqList, CovidReportUrlComonPath);
                                    labReport.ValidToPrint = true;
                                    labReport.BarCodeNumber = allBarCode[0];
                                    multipleReports.Add(labReport);
                                }
                                else
                                {
                                    throw new Exception("Multiple Barcode found for List of RequisitionID");
                                }
                            }
                        }

                        responseData.Results = multipleReports;
                        responseData.Status = "OK";

                    }
                    catch (Exception ex)
                    {
                        responseData.Status = "Failed";
                        responseData.ErrorMessage = ex.Message;
                    }

                }

                else if (reqType == "all-report-templates")
                {

                    List<LabReportTemplateModel> allReports = (from report in labDbContext.LabReportTemplates
                                                               where report.IsActive == true && report.TemplateType == ENUM_LabTemplateType.html// "html"
                                                               select report).ToList();


                    responseData.Status = "OK";
                    responseData.Results = allReports;
                }

                //to view report of one patient-visit
                else if (reqType == "viewReport-visit")
                {
                    var viewReport = (from req in labDbContext.Requisitions
                                      join tst in labDbContext.LabTests on req.LabTestId equals tst.LabTestId
                                      join temp in labDbContext.LabReportTemplates on tst.ReportTemplateId equals temp.ReportTemplateID
                                      where req.PatientVisitId == patientVisitId && tst.ReportTemplateId == temp.ReportTemplateID
                                      select new
                                      {
                                          TemplateName = temp.ReportTemplateShortName,
                                          Components = (from res in labDbContext.LabTestComponentResults
                                                        where req.RequisitionId == req.RequisitionId
                                                        select new
                                                        {
                                                            Component = res.ComponentName,
                                                            Value = res.Value,
                                                            Unit = res.Unit,
                                                            Range = res.Range,
                                                            Remarks = res.Remarks,
                                                            CreatedOn = res.CreatedOn,
                                                            RequisitionId = res.RequisitionId,
                                                            IsAbnormal = res.IsAbnormal
                                                        }).ToList()
                                      }).FirstOrDefault();

                    responseData.Results = viewReport;

                }
                //to get the requisitions of only the given visit. update it later if needed for somewhere else.--sud-9Aug'17

                else if (reqType == "visit-requisitions")
                {
                    //var labComponents = labDbContext.LabTestComponentResults.ToList();

                    //var reqsListTemp = (from req in labDbContext.Requisitions
                    //                    where req.PatientVisitId == patientVisitId
                    //                    && req.PatientId == patientId
                    //                    select new
                    //                    {
                    //                        TestId = req.LabTestId,
                    //                        TestName = req.LabTestName,
                    //                        req.RequisitionId,
                    //                        labComponents = labDbContext.LabTestComponentResults.Where(a => a.RequisitionId == req.RequisitionId).ToList()
                    //                    }).ToList();
                    //var reqsList = reqsListTemp.GroupBy(g => g.TestId).Select(go => new { go.Key, ult =  go.OrderByDescending(x => x.RequisitionId).Take(1) });

                    var reqsList = (from req in labDbContext.Requisitions
                                    where req.PatientVisitId == patientVisitId
                                    && req.PatientId == patientId
                                    select req
                                        )
                                        .GroupBy(x => x.LabTestId)
                                        .Select(g => new
                                        {
                                            g.Key,
                                            LatestRequisition = g.OrderByDescending(x => x.RequisitionId).FirstOrDefault()
                                        })
                                        .Select(x => new
                                        {
                                            TestId = x.Key,
                                            TestName = x.LatestRequisition.LabTestName,
                                            labComponents = labDbContext.LabTestComponentResults.Where(a => a.RequisitionId == x.LatestRequisition.RequisitionId).ToList()
                                        })
                                        .ToList();

                    responseData.Results = reqsList;
                }

                //to view report of a patient
                else if (reqType == "viewReport-patient")
                {
                    var viewReport = (from req in labDbContext.Requisitions
                                      join tst in labDbContext.LabTests on req.LabTestId equals tst.LabTestId
                                      join temp in labDbContext.LabReportTemplates on tst.ReportTemplateId
                                      equals temp.ReportTemplateID
                                      where req.PatientId == patientId && tst.ReportTemplateId == temp.ReportTemplateID
                                      select new
                                      {
                                          TemplateName = temp.ReportTemplateShortName,
                                          Components = (from res in labDbContext.LabTestComponentResults
                                                        where res.RequisitionId == req.RequisitionId
                                                        select new
                                                        {
                                                            Date = req.OrderDateTime,
                                                            Component = res.ComponentName,
                                                            Value = res.Value,
                                                            Unit = res.Unit,
                                                            Range = res.Range,
                                                            Remarks = res.Remarks,
                                                            CreatedOn = res.CreatedOn,
                                                            RequisitionId = res.RequisitionId,
                                                            IsAbnormal = res.IsAbnormal

                                                        }).ToList()
                                      }).FirstOrDefault();

                    responseData.Results = viewReport;

                }

                //getting some data to show the report ..when print is order..
                else if (patientId != 0 && templateId != 0)
                {

                    var printReport = (from x in labDbContext.Patients
                                       join y in labDbContext.Requisitions on x.PatientId equals y.PatientId
                                       join z in labDbContext.LabTestComponentResults on y.RequisitionId equals z.RequisitionId
                                       where y.PatientId == patientId
                                       select new
                                       {
                                           PatientName = y.PatientName,
                                           DateOfBrith = x.DateOfBirth,
                                           Gender = x.Gender,
                                           PatientCode = x.PatientCode,
                                           CreatedOn = y.OrderDateTime,
                                           ProviderId = y.ProviderId,
                                           ProvierName = y.ProviderName,
                                           //LabTestCategory = y.LabTest.LabTestGroups.LabCategory.LabCategoryName,
                                           //sud: lab-refactoring:23May'18

                                       }).ToList();
                    responseData.Results = printReport;


                }

                //getting all the test for search box
                else if (inputValue != null)
                {
                    string returnValue = string.Empty;
                    List<LabTestModel> testNameListFrmCache = (List<LabTestModel>)DanpheCache.Get("lab-test-all");

                    List<LabTestModel> filteredList = new List<LabTestModel>();
                    if (string.IsNullOrEmpty(inputValue))
                    {
                        filteredList = testNameListFrmCache;
                    }
                    else
                    {
                        filteredList = (from t in testNameListFrmCache
                                            //add
                                        where t.LabTestName.ToLower().Contains(inputValue.ToLower())
                                        select t).ToList();
                    }

                    var formatedResult = new DanpheHTTPResponse<List<LabTestModel>>() { Results = filteredList };
                    returnValue = DanpheJSONConvert.SerializeObject(formatedResult, true);
                    return returnValue;

                }

                else if (reqType == "allLabTests")
                {

                    // to store in cache
                    List<LabTestModel> testsFromCache = (List<LabTestModel>)DanpheCache.Get("lab-test-all");
                    if (testsFromCache == null)
                    {
                        testsFromCache = (new DanpheEMR.DalLayer.LabDbContext(connString)).LabTests.ToList();
                        DanpheCache.Add("lab-test-all", testsFromCache, 5);
                    }
                    responseData.Results = testsFromCache;

                }

                else if (reqType == "labTestListOfSelectedInpatient")
                {
                    //var currPatRequisitions = (from req in labDbContext.Requisitions
                    //                           join billItem in labDbContext.BillingTransactionItems on req.RequisitionId equals billItem.RequisitionId
                    //                           into tempItmList
                    //                           join dept in labDbContext.ServiceDepartment on billItem.ServiceDepartmentId equals dept.ServiceDepartmentId

                    //                           where (req.PatientId == patientId) && (req.PatientVisitId == patientVisitId) && (billItem.PatientId == patientId)
                    //                            && (req.BillingStatus.ToLower() == ENUM_BillingStatus.paid // "paid" 
                    //                            || req.BillingStatus.ToLower() == ENUM_BillingStatus.provisional) // "provisional")
                    //                            && (req.VisitType.ToLower() == ENUM_VisitType.inpatient) // "inpatient") 
                    //                            && dept.IntegrationName.ToLower() == "lab"
                    //                            && (!billItem.ReturnStatus.HasValue || billItem.ReturnStatus.Value == false)
                    //                           select new
                    //                           {
                    //                               BillingTransactionItemId = billItem.BillingTransactionItemId,
                    //                               RequisitionId = req.RequisitionId,
                    //                               PatientId = req.PatientId,
                    //                               PatientVisitId = req.PatientVisitId,
                    //                               LabTestName = req.LabTestName,
                    //                               LabTestId = req.LabTestId,
                    //                               ReportTemplateId = req.ReportTemplateId,
                    //                               LabTestSpecimen = req.LabTestSpecimen,
                    //                               ProviderId = req.ProviderId,
                    //                               ProviderName = req.ProviderName,
                    //                               RunNumberType = req.RunNumberType,
                    //                               BillingStatus = req.BillingStatus,
                    //                               OrderStatus = req.OrderStatus,
                    //                               OrderDateTime = req.OrderDateTime,
                    //                               IsReportGenerated = (
                    //                                   (from cmp in labDbContext.LabTestComponentResults
                    //                                    where cmp.RequisitionId == req.RequisitionId
                    //                                   && cmp.LabReportId.HasValue
                    //                                    select cmp).ToList().Count > 0
                    //                                )

                    //                           }).ToList();

                    //responseData.Results = currPatRequisitions;

                    string module = this.ReadQueryStringData("module");

                    PatientModel currPatient = labDbContext.Patients.Where(pat => pat.PatientId == patientId).FirstOrDefault();
                    if (currPatient != null)
                    {
                        string subDivName = (from pat in labDbContext.Patients
                                             join countrySubdiv in labDbContext.CountrySubdivisions
                                             on pat.CountrySubDivisionId equals countrySubdiv.CountrySubDivisionId
                                             where pat.PatientId == currPatient.PatientId
                                             select countrySubdiv.CountrySubDivisionName
                                          ).FirstOrDefault();

                        currPatient.CountrySubDivisionName = subDivName;
                        //remove relational property of patient//sud: 12May'18
                        //currPatient.BillingTransactionItems = null;
                    }

                    List<SqlParameter> paramList = new List<SqlParameter>() {
                        new SqlParameter("@patientId", patientId),
                        new SqlParameter("@patientVisitId", patientVisitId),
                        new SqlParameter("@moduleName", module)
                    };

                    DataTable patCreditItems = DALFunctions.GetDataTableFromStoredProc("SP_InPatient_Item_Details", paramList, labDbContext);


                    //create new anonymous type with patient information + Credit Items information : Anish:4May'18
                    var patCreditDetails = new
                    {
                        Patient = currPatient,
                        BillItems = patCreditItems
                    };
                    responseData.Status = "OK";
                    responseData.Results = patCreditDetails;

                }

                else if (reqType == "getSpecimen")
                {
                    LabRequisitionModel req = labDbContext.Requisitions.Where(val => val.RequisitionId == requisitionId).FirstOrDefault<LabRequisitionModel>();
                    if (req != null)
                    {
                        responseData.Results = req.LabTestSpecimen;
                    }
                }

                else if (reqType == "labRequisitionFromRequisitionIdList")
                {
                    List<Int64> reqIdList = DanpheJSONConvert.DeserializeObject<List<Int64>>(requisitionIdList);
                    List<LabRequisitionModel> allReq = new List<LabRequisitionModel>();
                    allReq = labDbContext.Requisitions.Where(req => reqIdList.Contains(req.RequisitionId)).ToList();

                    //foreach (var reqId in reqIdList)
                    //{
                    //LabRequisitionModel eachReq = new LabRequisitionModel();

                    //eachReq = labDbContext.Requisitions.Where(req => req.RequisitionId == reqId).FirstOrDefault();
                    // allReq.Add(eachReq);
                    //}

                    responseData.Results = allReq;
                }

                else if (reqType == "allTestListForExternalLabs")
                {
                    var defaultVendorId = (from vendor in labDbContext.LabVendors
                                           where vendor.IsDefault == true
                                           select vendor.LabVendorId).FirstOrDefault();
                    DateTime dtThirtyDays = DateTime.Now.AddDays(-30);
                    List<LabTestListWithVendor> allRequisitionsWithVendors = (from req in labDbContext.Requisitions
                                                                              join vendor in labDbContext.LabVendors on req.ResultingVendorId equals vendor.LabVendorId
                                                                              join pat in labDbContext.Patients on req.PatientId equals pat.PatientId
                                                                              join test in labDbContext.LabTests on req.LabTestId equals test.LabTestId
                                                                              where (req.OrderDateTime > dtThirtyDays)
                                                                              && req.OrderStatus == ENUM_LabOrderStatus.Pending //"pending" 

                                                                               && req.ResultingVendorId == defaultVendorId
                                                                              select new LabTestListWithVendor
                                                                              {
                                                                                  PatientName = pat.FirstName + " " + (string.IsNullOrEmpty(pat.MiddleName) ? "" : pat.MiddleName + " ") + pat.LastName,
                                                                                  RequisitionId = req.RequisitionId,
                                                                                  VendorName = vendor.VendorName,
                                                                                  TestName = test.LabTestName
                                                                              }).ToList();
                    responseData.Results = allRequisitionsWithVendors;
                }

                else if (reqType == "allTestListSendToExternalLabs")
                {
                    var defaultVendorId = (from vendor in labDbContext.LabVendors
                                           where vendor.IsDefault == true
                                           select vendor.LabVendorId).FirstOrDefault();
                    DateTime dtThirtyDays = DateTime.Now.AddDays(-30);
                    List<LabTestListWithVendor> allRequisitionsWithVendors = (from req in labDbContext.Requisitions
                                                                              join vendor in labDbContext.LabVendors on req.ResultingVendorId equals vendor.LabVendorId
                                                                              join pat in labDbContext.Patients on req.PatientId equals pat.PatientId
                                                                              join test in labDbContext.LabTests on req.LabTestId equals test.LabTestId
                                                                              where (req.OrderDateTime > dtThirtyDays)
                                                                              && req.ResultingVendorId != defaultVendorId
                                                                              select new LabTestListWithVendor
                                                                              {
                                                                                  PatientName = pat.FirstName + " " + (string.IsNullOrEmpty(pat.MiddleName) ? "" : pat.MiddleName + " ") + pat.LastName,
                                                                                  RequisitionId = req.RequisitionId,
                                                                                  VendorName = vendor.VendorName,
                                                                                  TestName = test.LabTestName
                                                                              }).ToList();
                    responseData.Results = allRequisitionsWithVendors;
                }

                else if (reqType == "all-lab-category")
                {
                    List<LabTestCategoryModel> allLabCategory = (from cat in labDbContext.LabTestCategory
                                                                 select cat
                                          ).ToList();
                    responseData.Results = allLabCategory;
                }
                else if (reqType == "get-lab-types")
                {
                    List<LabTypesModel> allLabtype = (from type in labDbContext.LabTypes
                                                      where type.IsActive == true
                                                      select type).ToList();
                    responseData.Results = allLabtype;
                }

                else if (reqType == "all-lab-specimen")
                {
                    var allSpecimen = (from cat in labDbContext.LabTestSpecimen
                                       select new
                                       {
                                           Name = cat.SpecimenName,
                                           IsSelected = false
                                       }).ToList();
                    responseData.Results = allSpecimen;
                }
                else if (reqType == "allSamplesCollectedData")
                {
                    var from = FromDate.Date;
                    var to = ToDate.Date;
                    var selectedLab = String.IsNullOrEmpty(activeLab) ? "" : activeLab;
                    List<SqlParameter> paramList = new List<SqlParameter>(){
                                                    new SqlParameter("@FromDate", from),
                                                    new SqlParameter("@ToDate", to),
                                                    new SqlParameter("@SelectedLab", selectedLab)
                                                };

                    DataTable samplesCollected = DALFunctions.GetDataTableFromStoredProc("SP_LAB_GetSamplesCollectedInfo", paramList, labDbContext);

                    var startTime = System.DateTime.Now;
                    foreach (var item in samplesCollected.Rows)
                    {
                        var a = 11;
                        var b = (a * 20) - 1000;
                        var c = b * 1000;
                    }
                    var diff = startTime.Subtract(System.DateTime.Now).TotalSeconds;


                    responseData.Status = "OK";
                    responseData.Results = samplesCollected;

                }
                else if (reqType == "allSmsApplicableTest")
                {
                    var from = FromDate.Date;
                    var to = ToDate.Date;

                    DataTable smsApplicableTests = labDbContext.GetAllSmsApplicableTests(from, to);
                    responseData.Results = smsApplicableTests;
                }
                else if (reqType == "getSMSMessage")
                {
                    var selectedId = Convert.ToInt64(requisitionId);
                    var patientData = GetSmsMessageAndNumberOfPatientByReqId(labDbContext, selectedId);
                    if (!(patientData.RequisitionId > 0) || !string.IsNullOrWhiteSpace(patientData.Message))
                    {
                        responseData.Results = patientData;
                    }
                    else
                    {
                        throw new Exception("Invalid Record");
                    }
                }
                responseData.Status = "OK";

            }
            catch (Exception ex)
            {
                responseData.Status = "Failed";
                responseData.ErrorMessage = ex.Message + " exception details:" + ex.ToString();
            }
            return DanpheJSONConvert.SerializeObject(responseData, true);
        }


        // POST api/values
        [HttpPost]
        public string Post()
        {
            DanpheHTTPResponse<object> responseData = new DanpheHTTPResponse<object>();
            try
            {
                string reqType = this.ReadQueryStringData("reqType");
                string specimenDataModel = this.ReadQueryStringData("specimenData");
                string ipStr = this.ReadPostData();
                LabDbContext labDbContext = new LabDbContext(connString);
                RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
                this.LabRunNumberSettings = (List<LabRunNumberSettingsModel>)DanpheCache.GetMasterData(MasterDataEnum.LabRunNumberSettings);

                if (reqType != null && reqType == "AddComponent")
                {
                    using (TransactionScope trans = new TransactionScope())
                    {
                        try
                        {
                            List<LabTestSpecimenModel> labSpecimenList = DanpheJSONConvert.DeserializeObject<List<LabTestSpecimenModel>>(specimenDataModel);
                            List<LabTestComponentResult> labComponentFromClient = DanpheJSONConvert.DeserializeObject<List<LabTestComponentResult>>(ipStr);

                            Int64 reqId = labComponentFromClient[0].RequisitionId;
                            int? templateId = labComponentFromClient[0].TemplateId;


                            LabRequisitionModel LabRequisition = labDbContext.Requisitions.Where(val => val.RequisitionId == reqId).FirstOrDefault();

                            if (LabRequisition.ReportTemplateId != templateId)
                            {
                                UpdateReportTemplateId(reqId, templateId, labDbContext, currentUser.EmployeeId);
                                labComponentFromClient.ForEach(cmp =>
                                {
                                    cmp.CreatedOn = DateTime.Now;
                                    cmp.CreatedBy = currentUser.EmployeeId;
                                    cmp.ResultGroup = cmp.ResultGroup.HasValue ? cmp.ResultGroup.Value : 1;
                                    labDbContext.LabTestComponentResults.Add(cmp);
                                });
                            }
                            else
                            {
                                labComponentFromClient.ForEach(cmp =>
                                {
                                    cmp.CreatedOn = DateTime.Now;
                                    cmp.CreatedBy = currentUser.EmployeeId;
                                    cmp.ResultGroup = cmp.ResultGroup.HasValue ? cmp.ResultGroup.Value : 1;
                                    labDbContext.LabTestComponentResults.Add(cmp);
                                });

                            }

                            labDbContext.SaveChanges();




                            //once the results are saved, put the status of 
                            List<Int64> distinctRequisitions = labComponentFromClient.Select(a => a.RequisitionId).Distinct().ToList();
                            string allReqIdListStr = "";

                            foreach (Int64 requisitionId in distinctRequisitions)
                            {
                                allReqIdListStr = allReqIdListStr + requisitionId + ",";
                                LabRequisitionModel dbRequisition = labDbContext.Requisitions
                                                                .Where(a => a.RequisitionId == requisitionId)
                                                                .FirstOrDefault<LabRequisitionModel>();

                                if (dbRequisition != null)
                                {
                                    dbRequisition.ResultAddedBy = currentUser.EmployeeId;
                                    dbRequisition.ResultAddedOn = System.DateTime.Now;
                                    dbRequisition.OrderStatus = ENUM_LabOrderStatus.ResultAdded;   // "result-added";
                                    labDbContext.Entry(dbRequisition).Property(a => a.OrderStatus).IsModified = true;
                                    labDbContext.Entry(dbRequisition).Property(a => a.ResultAddedBy).IsModified = true;
                                    labDbContext.Entry(dbRequisition).Property(a => a.ResultAddedOn).IsModified = true;

                                }
                            }

                            labDbContext.SaveChanges();


                            //Add specimen of culture test
                            if (labSpecimenList != null && labSpecimenList.Count > 0)
                            {
                                int ln = labSpecimenList.Count;
                                for (int i = 0; i < ln; i++)
                                {
                                    int? requisitId = labSpecimenList[i].RequisitionId;
                                    string specimen = labSpecimenList[i].Specimen;
                                    if (requisitId != null && requisitId > 0)
                                    {
                                        LabRequisitionModel labReq = labDbContext.Requisitions.Where(val => val.RequisitionId == requisitId).FirstOrDefault<LabRequisitionModel>();
                                        labReq.LabTestSpecimen = specimen;
                                        labDbContext.SaveChanges();
                                    }
                                }
                            }

                            allReqIdListStr = allReqIdListStr.Substring(0, (allReqIdListStr.Length - 1));

                            List<SqlParameter> paramList = new List<SqlParameter>(){
                                                    new SqlParameter("@allReqIds", allReqIdListStr),
                                                    new SqlParameter("@status", ENUM_BillingOrderStatus.Final)
                                                };
                            DataTable statusUpdated = DALFunctions.GetDataTableFromStoredProc("SP_Bill_OrderStatusUpdate", paramList, labDbContext);
                            trans.Complete();
                            responseData.Results = labComponentFromClient;
                            responseData.Status = "OK";
                        }
                        catch (Exception ex)
                        {
                            throw (ex);
                        }
                    }




                }
                else if (reqType == "FromBillingToRequisition")
                {
                    List<LabRequisitionModel> labReqListFromClient = DanpheJSONConvert.DeserializeObject<List<LabRequisitionModel>>(ipStr);
                    if (labReqListFromClient != null && labReqListFromClient.Count > 0)
                    {


                        PatientDbContext patientContext = new PatientDbContext(connString);
                        List<LabTestModel> allLabTests = labDbContext.LabTests.ToList();
                        int patId = labReqListFromClient[0].PatientId;
                        //get patient as querystring from client side rather than searching it from request's list.
                        PatientModel currPatient = patientContext.Patients.Where(p => p.PatientId == patId)
                            .FirstOrDefault<PatientModel>();

                        if (currPatient != null)
                        {

                            labReqListFromClient.ForEach(req =>
                            {
                                LabTestModel labTestdb = allLabTests.Where(a => a.LabTestId == req.LabTestId).FirstOrDefault<LabTestModel>();
                                //get PatientId from clientSide
                                if (labTestdb.IsValidForReporting == true)
                                {
                                    req.LabTestSpecimen = labTestdb.LabTestSpecimen;
                                    req.LabTestSpecimenSource = labTestdb.LabTestSpecimenSource;
                                    req.OrderStatus = ENUM_LabOrderStatus.Active; //"active";
                                    req.LOINC = "LOINC Code";
                                    req.RunNumberType = labTestdb.RunNumberType;
                                    //req.PatientVisitId = visitId;//assign above visitid to this requisition.
                                    if (String.IsNullOrEmpty(currPatient.MiddleName))
                                        req.PatientName = currPatient.FirstName + " " + currPatient.LastName;
                                    else
                                        req.PatientName = currPatient.FirstName + " " + currPatient.MiddleName + " " + currPatient.LastName;

                                    req.OrderDateTime = DateTime.Now;
                                    labDbContext.Requisitions.Add(req);
                                }
                            });
                            labDbContext.SaveChanges();
                            responseData.Results = labReqListFromClient;
                            responseData.Status = "OK";
                        }
                        responseData.Status = "OK";
                    }
                    else
                    {
                        responseData.Status = "Failed";
                        responseData.ErrorMessage = "Invalid input request.";
                    }
                }
                else if (reqType == "addNewRequisitions") //comes here from doctor and nurse orders.
                {
                    List<LabRequisitionModel> labReqListFromClient = DanpheJSONConvert.DeserializeObject<List<LabRequisitionModel>>(ipStr);
                    LabVendorsModel defaultVendor = labDbContext.LabVendors.Where(val => val.IsDefault == true).FirstOrDefault();


                    if (labReqListFromClient != null && labReqListFromClient.Count > 0)
                    {
                        PatientDbContext patientContext = new PatientDbContext(connString);
                        List<LabTestModel> allLabTests = labDbContext.LabTests.ToList();
                        int patId = labReqListFromClient[0].PatientId;
                        //get patient as querystring from client side rather than searching it from request's list.
                        PatientModel currPatient = patientContext.Patients.Where(p => p.PatientId == patId)
                            .FirstOrDefault<PatientModel>();

                        if (currPatient != null)
                        {

                            labReqListFromClient.ForEach(req =>
                            {
                                req.ResultingVendorId = defaultVendor.LabVendorId;
                                LabTestModel labTestdb = allLabTests.Where(a => a.LabTestId == req.LabTestId).FirstOrDefault<LabTestModel>();
                                //get PatientId from clientSide
                                if (labTestdb.IsValidForReporting == true)
                                {
                                    req.CreatedOn = req.OrderDateTime = System.DateTime.Now;
                                    req.ReportTemplateId = labTestdb.ReportTemplateId ?? default(int);
                                    req.LabTestSpecimen = null;
                                    req.LabTestSpecimenSource = null;
                                    req.LabTestName = labTestdb.LabTestName;
                                    req.RunNumberType = labTestdb.RunNumberType;
                                    //req.OrderStatus = "active";
                                    req.LOINC = "LOINC Code";
                                    req.BillCancelledBy = null;
                                    req.BillCancelledOn = null;
                                    if (req.ProviderId != null && req.ProviderId != 0)
                                    {
                                        var emp = labDbContext.Employee.Where(a => a.EmployeeId == req.ProviderId).FirstOrDefault();
                                        req.ProviderName = emp.FullName;
                                    }

                                    //req.PatientVisitId = visitId;//assign above visitid to this requisition.
                                    if (String.IsNullOrEmpty(currPatient.MiddleName))
                                        req.PatientName = currPatient.FirstName + " " + currPatient.LastName;
                                    else
                                        req.PatientName = currPatient.FirstName + " " + currPatient.MiddleName + " " + currPatient.LastName;

                                    req.OrderDateTime = DateTime.Now;
                                    labDbContext.Requisitions.Add(req);
                                    labDbContext.SaveChanges();
                                }
                            });

                            responseData.Results = labReqListFromClient;
                            responseData.Status = "OK";
                        }
                    }
                    else
                    {
                        responseData.Status = "Failed";
                        responseData.ErrorMessage = "Invalid input request.";
                    }

                }
                else if (reqType == "add-labReport")
                {
                    var requiredParam = (from param in labDbContext.AdminParameters
                                         where (param.ParameterName == "CovidTestName" || param.ParameterName == "EnableCovidReportPDFUploadToGoogle" || param.ParameterName == "AllowLabReportToPrintOnProvisional")
                                         && (param.ParameterGroupName.ToLower() == "common" || param.ParameterGroupName.ToLower() == "lab")
                                         select param).ToList();

                    string covidParameter = (from param in requiredParam
                                             where param.ParameterName == "CovidTestName"
                                             && param.ParameterGroupName.ToLower() == "common"
                                             select param.ParameterValue).FirstOrDefault();

                    string covidReportUploadEnabled = (from param in requiredParam
                                                       where param.ParameterName == "EnableCovidReportPDFUploadToGoogle"
                                                       && param.ParameterGroupName.ToLower() == "lab"
                                                       select param.ParameterValue).FirstOrDefault();

                    string covidTestName = "";
                    if (covidParameter != null)
                    {
                        var data = (JObject)JsonConvert.DeserializeObject(covidParameter);
                        covidTestName = data["DisplayName"].Value<string>();
                    }

                    using (TransactionScope trans = new TransactionScope())
                    {
                        try
                        {
                            LabReportModel labReport = DanpheJSONConvert.DeserializeObject<LabReportModel>(ipStr);


                            var VerificationEnabled = labReport.VerificationEnabled;
                            labReport.ReportingDate = DateTime.Now;
                            labReport.CreatedBy = currentUser.EmployeeId;
                            labReport.CreatedOn = labReport.ReportingDate;

                            List<Int64> reqIdList = new List<Int64>();
                            bool IsValidToPrint = true;


                            labDbContext.LabReports.Add(labReport);

                            labDbContext.SaveChanges();

                            string allReqIdListStr = "";

                            if (labReport.LabReportId != 0)
                            {
                                foreach (var componentId in labReport.ComponentIdList)
                                {
                                    LabTestComponentResult component = labDbContext.LabTestComponentResults.Where(cmp => cmp.TestComponentResultId == componentId).FirstOrDefault();
                                    reqIdList.Add(component.RequisitionId);
                                    component.LabReportId = labReport.LabReportId;
                                    labDbContext.Entry(component).Property(a => a.LabReportId).IsModified = true;
                                }
                                labDbContext.SaveChanges();

                                var reqIdToUpdate = reqIdList.Distinct().ToList();

                                var parameterData = (from parameter in requiredParam
                                                     where parameter.ParameterGroupName.ToLower() == "lab"
                                                     && parameter.ParameterName == "AllowLabReportToPrintOnProvisional"
                                                     select parameter.ParameterValue).FirstOrDefault();

                                foreach (var reqId in reqIdToUpdate)
                                {
                                    allReqIdListStr = allReqIdListStr + reqId + ",";
                                    LabRequisitionModel requisitionItem = labDbContext.Requisitions.Where(val => val.RequisitionId == reqId).FirstOrDefault();
                                    if (VerificationEnabled != true)
                                    {
                                        requisitionItem.OrderStatus = ENUM_LabOrderStatus.ReportGenerated;// "report-generated";
                                    }
                                    requisitionItem.LabReportId = labReport.LabReportId;

                                    //for covidtest create empty report in google drive
                                    if (requisitionItem.LabTestName == covidTestName)
                                    {
                                        if ((covidReportUploadEnabled != null) && (covidReportUploadEnabled == "true" || covidReportUploadEnabled == "1"))
                                        {
                                            var fileName = "LabCovidReports_" + requisitionItem.RequisitionId + "_" + DateTime.Now.ToString("yyyyMMdd-HHMMss") + ".pdf";
                                            var retData = GoogleDriveFileUpload.UploadNewFile(fileName);
                                            if (!string.IsNullOrEmpty(retData.FileId))
                                            {
                                                requisitionItem.CovidFileName = fileName;
                                                requisitionItem.GoogleFileIdForCovid = retData.FileId;
                                                labReport.CovidFileUrl = CovidReportUrlComonPath.Replace("GGLFILEUPLOADID", retData.FileId);
                                            }
                                        }
                                    }

                                    //give provisional billing for outpatiient to print
                                    if (parameterData != null && (parameterData.ToLower() == "true" || parameterData == "1"))
                                    {
                                        if (requisitionItem.BillingStatus.ToLower() == ENUM_BillingStatus.provisional) // "provisional")
                                        {
                                            IsValidToPrint = true;
                                        }

                                    }
                                    else
                                    {
                                        if ((requisitionItem.VisitType.ToLower() == ENUM_VisitType.outpatient // "outpatient" 
                                            || requisitionItem.VisitType.ToLower() == ENUM_VisitType.emergency) // "emergency") 
                                            && requisitionItem.BillingStatus.ToLower() == ENUM_BillingStatus.provisional) // "provisional")
                                        {
                                            IsValidToPrint = false;
                                        }
                                    }

                                    if (requisitionItem.RunNumberType.ToLower() == ENUM_LabRunNumType.histo // "histo" 
                                        || requisitionItem.RunNumberType.ToLower() == ENUM_LabRunNumType.cyto) // "cyto")
                                    {
                                        LabReportModel report = labDbContext.LabReports.Where(rep => rep.LabReportId == labReport.LabReportId).FirstOrDefault();
                                        report.ReceivingDate = requisitionItem.OrderDateTime;
                                        labReport.ReceivingDate = report.ReceivingDate;
                                    }
                                }
                                labDbContext.SaveChanges();



                                //if (docPatPortalSync)
                                //{
                                //    DocPatPortalBL.PostLabFinalReport(labReport, labDbContext);
                                //}
                            }

                            allReqIdListStr = allReqIdListStr.Substring(0, (allReqIdListStr.Length - 1));

                            //List<SqlParameter> paramList = new List<SqlParameter>(){
                            //                        new SqlParameter("@allReqIds", allReqIdListStr),
                            //                        new SqlParameter("@status", ENUM_BillingOrderStatus.Final)
                            //                    };
                            //DataTable statusUpdated = DALFunctions.GetDataTableFromStoredProc("SP_Bill_OrderStatusUpdate", paramList, labDbContext);
                            trans.Complete();

                            labReport.ValidToPrint = IsValidToPrint;

                            responseData.Results = labReport;
                            responseData.Status = "OK";
                        }
                        catch (Exception ex)
                        {
                            throw (ex);
                        }
                    }
                }
                else if (reqType == "postSMS")
                {
                    try
                    {

                        var reqIdlist = ipStr;
                        var selectedId = Convert.ToInt64(ipStr);

                        var patientData = GetSmsMessageAndNumberOfPatientByReqId(labDbContext, selectedId);
                        if (patientData != null)
                        {
                            var payLoad = HttpUtility.UrlEncode(patientData.Message);

                            var smsParamList = labDbContext.AdminParameters.Where(p => (p.ParameterGroupName.ToLower() == "lab") && ((p.ParameterName == "SmsParameter") || (p.ParameterName == "LabSmsProviderName"))).Select(d => new { d.ParameterValue, d.ParameterName }).ToList();
                            var providerName = smsParamList.Where(s => s.ParameterName == "LabSmsProviderName").Select(d => d.ParameterValue).FirstOrDefault() ?? "Sparrow";
                            var smsParam = smsParamList.Where(s => s.ParameterName == "SmsParameter").Select(d => d.ParameterValue).FirstOrDefault() ?? "[]";
                            var smsParamObj = JsonConvert.DeserializeObject<List<dynamic>>(smsParam);

                            var selectedProviderDetail = smsParamObj.Where(p => p["SmsProvider"] == providerName).FirstOrDefault();
                            if (selectedProviderDetail != null)
                            {
                                string key = selectedProviderDetail["Token"];

                                if (providerName == "LumbiniTech")
                                {
                                    string url = selectedProviderDetail["Url"];
                                    url = url.Replace("SMSKEY", key);
                                    url = url.Replace("SMSPHONENUMBER", patientData.PhoneNumber);
                                    url = url.Replace("SMSMESSAGE", payLoad);

                                    var request = (HttpWebRequest)WebRequest.Create(url);
                                    request.ContentType = "application/json";
                                    request.Method = "GET";
                                    var httpResponse = (HttpWebResponse)request.GetResponse();
                                    using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                                    {
                                        var responseMsg = streamReader.ReadToEnd(); // error message or success message catched here

                                        if (httpResponse.StatusCode == HttpStatusCode.OK)
                                        {
                                            LabSMSModel data = new LabSMSModel();
                                            data.RequisitionId = Convert.ToInt32(selectedId);
                                            data.Message = payLoad;
                                            data.CreatedOn = System.DateTime.Now;
                                            data.CreatedBy = currentUser.EmployeeId;

                                            labDbContext.LabSms.Add(data);
                                            labDbContext.SaveChanges();

                                            List<SqlParameter> paramList = new List<SqlParameter>() { new SqlParameter("@RequistionIds", reqIdlist) };
                                            DataSet dts = DALFunctions.GetDatasetFromStoredProc("SP_LAB_Update_Test_SmsStatus", paramList, labDbContext);
                                            labDbContext.SaveChanges();

                                            responseData.Status = "OK";
                                        }
                                        else
                                        {
                                            responseData.Status = "Failed";
                                        }
                                    }


                                    //lumbinitech implementation

                                }
                                else if (providerName == "Sparrow")
                                {
                                    //sparrow implementation
                                }
                            }
                            else
                            {
                                responseData.Status = "Failed";
                            }

                        }
                        else
                        {
                            responseData.Status = "Failed";
                        }

                    }
                    catch (Exception ex)
                    {
                        responseData.Status = "Failed";
                        responseData.ErrorMessage = ex.Message + " exception details:" + ex.ToString();
                        throw ex;
                    }

                }
                else if (reqType == "sendCovidPdfReport")
                {
                    var inputData = this.ReadPostData();
                    var folderPath = CovidReportFileUploadPath;
                    byte[] data = Convert.FromBase64String(inputData);
                    long reqId = 0;
                    var reqIdStr = this.ReadQueryStringData("requisitionId");
                    reqId = Convert.ToInt64(reqIdStr);
                    try
                    {
                        if (folderPath != null)
                        {
                            if (!System.IO.Directory.Exists(folderPath))
                            {
                                System.IO.Directory.CreateDirectory(folderPath);
                            }

                            var currReq = labDbContext.Requisitions.Where(r => r.RequisitionId == reqId).FirstOrDefault();
                            if (currReq != null)
                            {
                                // var retData = GoogleDriveFileUpload.UploadNewFile(fileName);
                                var fileName = "LabCovidReports_" + currReq.RequisitionId + "_" + DateTime.Now.ToString("yyyyMMdd-HHMMss") + ".pdf";
                                if (!string.IsNullOrWhiteSpace(currReq.GoogleFileIdForCovid))
                                {
                                    //if (!System.IO.File.Exists(folderPath + '\\' + currReq.CovidFileName)){}
                                    System.IO.File.WriteAllBytes(folderPath + '\\' + currReq.CovidFileName, data);
                                    var retData = GoogleDriveFileUpload.UpdateFileById(currReq.GoogleFileIdForCovid, currReq.CovidFileName, newMimeType: "application/pdf");
                                    responseData.Status = "OK";
                                    responseData.Results = 1;
                                }
                                else
                                {
                                    System.IO.File.WriteAllBytes(folderPath + '\\' + fileName, data);
                                    var retData = GoogleDriveFileUpload.UploadNewFile(fileName);
                                    currReq.GoogleFileIdForCovid = retData.FileId;
                                    currReq.CovidFileName = fileName;
                                    labDbContext.SaveChanges();
                                    var retDataNew = GoogleDriveFileUpload.UpdateFileById(retData.FileId, fileName, newMimeType: "application/pdf");
                                    responseData.Status = "OK";
                                    responseData.Results = 1;
                                }

                                if (responseData.Status == "OK")
                                {
                                    currReq.IsFileUploaded = true;
                                    currReq.UploadedBy = currentUser.EmployeeId;
                                    currReq.UploadedOn = System.DateTime.Now;
                                    labDbContext.Entry(currReq).Property(a => a.IsFileUploaded).IsModified = true;
                                    labDbContext.Entry(currReq).Property(a => a.UploadedBy).IsModified = true;
                                    labDbContext.Entry(currReq).Property(a => a.UploadedOn).IsModified = true;
                                    labDbContext.SaveChanges();
                                }

                            }
                            else
                            {
                                responseData.Status = "Failed";
                                throw new Exception("Cannot find the test");
                            }

                        }

                    }
                    catch (Exception ex)
                    {
                        responseData.Status = "Failed";
                        responseData.ErrorMessage = ex.Message + " exception details:" + ex.ToString();
                        throw ex;
                    }
                }
                else if (reqType == "updateSampleCodeAutomatically")
                {
                    string str = this.ReadPostData();
                    List<PatientLabSampleVM> labTests = DanpheJSONConvert.DeserializeObject<List<PatientLabSampleVM>>(str);//this will come from client side--after parsing.

                    //sample code for All Tests in Current Requests will be same.

                    if (labTests != null)
                    {
                        try
                        {
                            var singlePat = labTests[0];
                            var currDate = System.DateTime.Now;
                            var hasIns = singlePat.HasInsurance.HasValue ? singlePat.HasInsurance.Value : false;
                            int patId = singlePat.PatientId;
                            if ((patId == 0) || (patId < 0)) { throw new Exception("Patient not selected properly."); }
                            var sampleDetail = this.GenerateLabSampleCode(labDbContext, singlePat.RunNumberType.ToLower(), singlePat.VisitType.ToLower(), patId, currDate, hasIns);

                            var latestSample = new
                            {
                                SampleCode = sampleDetail.SampleCode,
                                SampleNumber = sampleDetail.SampleNumber,
                                BarCodeNumber = sampleDetail.BarCodeNumber,
                                SampleLetter = sampleDetail.SampleLetter,
                                ExistingBarCodeNumbersOfPatient = sampleDetail.ExistingBarCodeNumbersOfPatient
                            };

                            foreach (var item in labTests)
                            {
                                item.SampleCode = sampleDetail.SampleNumber;
                                item.BarCodeNumber = sampleDetail.BarCodeNumber;
                                item.SampleCreatedOn = currDate;
                            }
                            if (sampleDetail.SampleNumber.HasValue && (sampleDetail.SampleNumber > 0))
                            {
                                var data = UpdateSampleCode(labDbContext, labTests, currentUser);
                                responseData.Results = new
                                {
                                    LatestSampleData = latestSample,
                                    FormattedSampleCode = data.FormattedSampleCode,
                                    BarCodeNumber = data.BarCodeNumber,
                                    SampleCollectedOnDateTime = data.SampleCollectedOnDateTime
                                };
                                responseData.Status = "OK";
                            }
                            else
                            {
                                responseData.Status = "Failed";
                                throw new Exception("Cannot update sample code now. Please try again later.");
                            }

                        }
                        catch (Exception ex)
                        {
                            throw (ex);
                        }


                    }
                }
                else if (reqType == "saveLabSticker")
                {
                    //ipDataString is input (HTML string)
                    if (ipStr.Length > 0)
                    {
                        ///api/Billing?reqType=saveLabSticker&PrinterName=sticker1809003399&FilePath=C:\DanpheHealthInc_PvtLtd_Files\Print\
                        //Read html

                        string PrinterName = this.ReadQueryStringData("PrinterName");
                        //string FileName = this.ReadQueryStringData("fileName");
                        int noOfPrints = Convert.ToInt32(this.ReadQueryStringData("numOfCopies"));

                        var parameter = (from param in labDbContext.AdminParameters
                                         where param.ParameterGroupName.ToLower() == "lab" &&
                                         param.ParameterName == "LabStickerSettings"
                                         select param.ParameterValue).FirstOrDefault();

                        List<LabStickerParam> paramArray = new List<LabStickerParam>();

                        if (parameter != null)
                        {
                            paramArray = DanpheJSONConvert.DeserializeObject<List<LabStickerParam>>(parameter);
                        }

                        string FolderPath = this.ReadQueryStringData("filePath");

                        if (noOfPrints == 0)
                        {
                            noOfPrints = 1;
                        }

                        for (int i = 0; i < noOfPrints; i++)
                        {
                            //index:i, taken in filename 
                            var fileFullName = "Lab" + "_user_" + currentUser.EmployeeId + "_" + (i + 1) + ".html";
                            byte[] htmlbytearray = System.Text.Encoding.ASCII.GetBytes(ipStr);
                            //saving file to default folder, html file need to be delete after print is called.
                            System.IO.File.WriteAllBytes(@FolderPath + fileFullName, htmlbytearray);

                        }

                        responseData.Status = "OK";
                        responseData.Results = 1;
                    }
                }
                else if (reqType == "sendEmail")
                {
                    string str = this.ReadPostData();
                    MasterDbContext masterContext = new MasterDbContext(base.connString);
                    LabEmailModel EmailModel = JsonConvert.DeserializeObject<LabEmailModel>(str);
                    var apiKey = (from param in masterContext.CFGParameters
                                  where param.ParameterGroupName.ToLower() == "common" && param.ParameterName == "APIKeyOfEmailSendGrid"
                                  select param.ParameterValue
                                  ).FirstOrDefault();

                    if (!EmailModel.SendPdf)
                    {
                        EmailModel.PdfBase64 = null;
                        EmailModel.AttachmentFileName = null;
                    }

                    if (!EmailModel.SendHtml)
                    {
                        EmailModel.PlainContent = "";
                    }

                    Task<string> response = _emailService.SendEmail(EmailModel.SenderEmailAddress, EmailModel.EmailList,
                        EmailModel.SenderTitle, EmailModel.Subject, EmailModel.PlainContent,
                        EmailModel.HtmlContent, EmailModel.PdfBase64, EmailModel.AttachmentFileName,
                        EmailModel.ImageAttachments, apiKey);

                    response.Wait();

                    if (response.Result == "OK")
                    {
                        EmailSendDetailModel sendEmail = new EmailSendDetailModel();
                        foreach (var eml in EmailModel.EmailList)
                        {
                            sendEmail.SendBy = currentUser.EmployeeId;
                            sendEmail.SendOn = System.DateTime.Now;
                            sendEmail.SendToEmail = eml;
                            sendEmail.EmailSubject = EmailModel.Subject;
                            masterContext.SendEmailDetails.Add(sendEmail);
                            masterContext.SaveChanges();
                        }

                        responseData.Status = "OK";

                    }
                    else
                    {
                        responseData.Status = "Failed";
                    }


                }

            }
            catch (Exception ex)
            {
                responseData.Status = "Failed";
                responseData.ErrorMessage = ex.Message + " exception details:" + ex.ToString();
            }
            return DanpheJSONConvert.SerializeObject(responseData, true);
        }


        // PUT api/values/5
        [Route("updateFileUploadStatus")]
        [HttpPut]
        public IActionResult updateFileUploadStatus(string requisitionIdList)
        {

            DanpheHTTPResponse<object> responseData = new DanpheHTTPResponse<object>();
            LabDbContext labDbContext = new LabDbContext(connString);
            try
            {
                RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
                List<Int64> reqIdList = DanpheJSONConvert.DeserializeObject<List<Int64>>(requisitionIdList);
                List<LabRequisitionModel> model = labDbContext.Requisitions.Where(a => reqIdList.Contains(a.RequisitionId)).ToList();
                model.ForEach((singleModel) =>
                {
                    singleModel.IsFileUploadedToTeleMedicine = true;
                    singleModel.ModifiedBy = currentUser.UserId;
                    singleModel.ModifiedOn = DateTime.Now;
                    singleModel.UploadedByToTeleMedicine = currentUser.UserId;
                    singleModel.UploadedOnToTeleMedicine = DateTime.Now;
                    labDbContext.Entry(singleModel).Property(a => a.ModifiedBy).IsModified = true;
                    labDbContext.Entry(singleModel).Property(a => a.ModifiedOn).IsModified = true;
                    labDbContext.Entry(singleModel).Property(a => a.IsFileUploadedToTeleMedicine).IsModified = true;
                    labDbContext.Entry(singleModel).Property(a => a.UploadedByToTeleMedicine).IsModified = true;
                    labDbContext.Entry(singleModel).Property(a => a.UploadedOnToTeleMedicine).IsModified = true;
                    labDbContext.SaveChanges();
                });
                responseData.Results = model;
                responseData.Status = "OK";
                return Ok(responseData);
            }
            catch (Exception ex)
            {
                responseData.Status = "Failed";
                responseData.ErrorMessage = ex.Message + " exception details:" + ex.ToString();
                return BadRequest(responseData);
            }
        }

        [HttpPut]
        public string Put()
        {

            DanpheHTTPResponse<object> responseData = new DanpheHTTPResponse<object>();
            try
            {
                //update Sample in LAB_Requisition
                string str = this.ReadPostData();
                string reqType = this.ReadQueryStringData("reqType");
                string billstatus = this.ReadQueryStringData("billstatus");
                string comments = this.ReadQueryStringData("comments");
                string labReqIdList = this.ReadQueryStringData("requisitionIdList");
                int vendorId = ToInt(this.ReadQueryStringData("vendorId"));
                //sud:22Aug'18 --it was giving error when trying Int.Parse(),  so use Convert.ToInt instead.
                int referredById = Convert.ToInt32(this.ReadQueryStringData("id"));

                int? SampleCode = ToInt(this.ReadQueryStringData("SampleCode"));
                int? PrintedReportId = ToInt(this.ReadQueryStringData("reportId"));
                int printid = 0;
                int.TryParse(this.ReadQueryStringData("printid"), out printid);
                RbacUser currentUser = HttpContext.Session.Get<RbacUser>("currentuser");
                //int CurrentUser = 1;
                //int.TryParse(this.ReadQueryStringData("CurrentUser"), out CurrentUser);
                int? RunNumber = ToInt(this.ReadQueryStringData("RunNumber"));
                LabDbContext labDbContext = new LabDbContext(connString);
                this.LabRunNumberSettings = (List<LabRunNumberSettingsModel>)DanpheCache.GetMasterData(MasterDataEnum.LabRunNumberSettings);





                //used in collect sample page.
                //we're sending test list instead of reqId list because we may have different sample codes agaist different test if we use use last sample code feature.
                if (reqType == "updateSampleCode")
                {

                    List<PatientLabSampleVM> labTests = DanpheJSONConvert.DeserializeObject<List<PatientLabSampleVM>>(str); ;//this will come from client side--after parsing.

                    //sample code for All Tests in Current Requests will be same.

                    if (labTests != null)
                    {
                        try
                        {
                            var data = UpdateSampleCode(labDbContext, labTests, currentUser);

                            responseData.Results = new { FormattedSampleCode = data.FormattedSampleCode, BarCodeNumber = data.BarCodeNumber, SampleCollectedOnDateTime = data.SampleCollectedOnDateTime };
                            responseData.Status = "OK";
                        }
                        catch (Exception ex)
                        {
                            throw (ex);
                        }
                    }


                }
                //ashim: 20Sep2018
                //used in view report page page.
                else if (reqType == "updae-sample-code-reqId")
                {
                    List<Int64> reqIdList = DanpheJSONConvert.DeserializeObject<List<Int64>>(str);

                    DateTime? SampleDate = Convert.ToDateTime(this.ReadQueryStringData("SampleDate"));
                    string runNumberType = this.ReadQueryStringData("runNumberType");
                    string patVisitType = this.ReadQueryStringData("visitType");


                    Int64? existingBarCodeNum = null;


                    LabRequisitionModel requisition = new LabRequisitionModel();
                    Int64 lastBarCodeNum = (from bar in labDbContext.LabBarCode
                                            select bar.BarCodeNumber).DefaultIfEmpty(0).Max();
                    //if barcode number is not found then start from 1million (10 lakhs)
                    Int64 newBarCodeNumber = lastBarCodeNum != 0 ? lastBarCodeNum + 1 : 1000000;

                    string visitType = null;
                    string RunNumberType = null;
                    Int64? LabBarCodeNum = null;
                    long singleReqId = reqIdList[0];
                    var singleReq = labDbContext.Requisitions.Where(a => a.RequisitionId == singleReqId).Select(s => new { s.HasInsurance, s.PatientId }).FirstOrDefault();
                    bool underInsurance = singleReq.HasInsurance;

                    //Get the GroupingIndex From visitType and Run Number Type
                    var currentSetting = (from runNumSetting in LabRunNumberSettings
                                          where runNumSetting.VisitType.ToLower() == patVisitType.ToLower()
                                          && runNumSetting.RunNumberType.ToLower() == runNumberType.ToLower()
                                          && runNumSetting.UnderInsurance == underInsurance
                                          select runNumSetting
                                         ).FirstOrDefault();

                    //get the requisition with same Run number
                    List<SqlParameter> paramList = new List<SqlParameter>() {
                        new SqlParameter("@SampleDate", SampleDate),
                        new SqlParameter("@SampleCode", RunNumber),
                        new SqlParameter("@PatientId", singleReq.PatientId),
                        new SqlParameter("@GroupingIndex", currentSetting.RunNumberGroupingIndex)};
                    DataSet dts = DALFunctions.GetDatasetFromStoredProc("SP_LAB_GetPatientExistingRequisition_With_SameRunNumber", paramList, labDbContext);

                    List<LabRequisitionModel> esistingReqOfPat = new List<LabRequisitionModel>();

                    if (dts.Tables.Count > 0)
                    {
                        var strPatExistingReq = JsonConvert.SerializeObject(dts.Tables[0]);
                        esistingReqOfPat = DanpheJSONConvert.DeserializeObject<List<LabRequisitionModel>>(strPatExistingReq);
                        requisition = (esistingReqOfPat.Count > 0) ? esistingReqOfPat[0] : null;
                    }
                    else
                    {
                        requisition = null;
                    }

                    if (requisition != null)
                    {
                        existingBarCodeNum = requisition.BarCodeNumber;
                        LabBarCodeModel newBarCode = labDbContext.LabBarCode
                                                            .Where(c => c.BarCodeNumber == existingBarCodeNum)
                                                            .FirstOrDefault<LabBarCodeModel>();
                        newBarCode.IsActive = true;

                        labDbContext.Entry(newBarCode).Property(a => a.IsActive).IsModified = true;

                        labDbContext.SaveChanges();

                        SampleDate = requisition.SampleCreatedOn;
                    }
                    else
                    {
                        if (existingBarCodeNum == null)
                        {
                            LabBarCodeModel barCode = new LabBarCodeModel();
                            barCode.BarCodeNumber = newBarCodeNumber;
                            barCode.IsActive = true;
                            barCode.CreatedBy = currentUser.EmployeeId;
                            barCode.CreatedOn = System.DateTime.Now;
                            labDbContext.LabBarCode.Add(barCode);
                            labDbContext.SaveChanges();
                        }

                    }


                    foreach (var reqId in reqIdList)
                    {
                        LabRequisitionModel dbRequisition = labDbContext.Requisitions
                                                        .Where(a => a.RequisitionId == reqId)
                                                        .FirstOrDefault<LabRequisitionModel>();


                        if (dbRequisition != null)
                        {
                            List<LabRequisitionModel> allReqWithCurrBarcode = labDbContext.Requisitions
                                                                                .Where(r => r.BarCodeNumber == dbRequisition.BarCodeNumber)
                                                                                .ToList();


                            if (allReqWithCurrBarcode.Count == reqIdList.Count)
                            {
                                LabBarCodeModel oldBarCode = labDbContext.LabBarCode
                                                            .Where(c => c.BarCodeNumber == dbRequisition.BarCodeNumber)
                                                            .FirstOrDefault<LabBarCodeModel>();
                                oldBarCode.IsActive = false;
                                oldBarCode.ModifiedBy = currentUser.EmployeeId;
                                oldBarCode.ModifiedOn = System.DateTime.Now;
                                labDbContext.Entry(oldBarCode).Property(a => a.ModifiedBy).IsModified = true;
                                labDbContext.Entry(oldBarCode).Property(a => a.ModifiedOn).IsModified = true;
                                labDbContext.Entry(oldBarCode).Property(a => a.IsActive).IsModified = true;
                            }

                            dbRequisition.SampleCode = RunNumber;
                            dbRequisition.SampleCodeFormatted = GetSampleCodeFormatted(RunNumber, SampleDate.Value, patVisitType, runNumberType);
                            dbRequisition.SampleCreatedOn = SampleDate;
                            dbRequisition.SampleCollectedOnDateTime = System.DateTime.Now;
                            dbRequisition.SampleCreatedBy = currentUser.EmployeeId;
                            dbRequisition.ModifiedBy = currentUser.EmployeeId;
                            dbRequisition.ModifiedOn = System.DateTime.Now;
                            dbRequisition.BarCodeNumber = existingBarCodeNum != null ? existingBarCodeNum : newBarCodeNumber;
                            visitType = dbRequisition.VisitType;
                            RunNumberType = dbRequisition.RunNumberType;
                            LabBarCodeNum = existingBarCodeNum != null ? existingBarCodeNum : newBarCodeNumber;
                        }

                        labDbContext.Entry(dbRequisition).Property(a => a.ModifiedBy).IsModified = true;
                        labDbContext.Entry(dbRequisition).Property(a => a.ModifiedOn).IsModified = true;
                        labDbContext.Entry(dbRequisition).Property(a => a.SampleCode).IsModified = true;
                        labDbContext.Entry(dbRequisition).Property(a => a.SampleCodeFormatted).IsModified = true;
                        labDbContext.Entry(dbRequisition).Property(a => a.SampleCreatedBy).IsModified = true;
                        labDbContext.Entry(dbRequisition).Property(a => a.SampleCreatedOn).IsModified = true;
                        labDbContext.Entry(dbRequisition).Property(a => a.SampleCollectedOnDateTime).IsModified = true;
                        labDbContext.Entry(dbRequisition).Property(a => a.BarCodeNumber).IsModified = true;
                    }


                    labDbContext.SaveChanges();
                    string formattedSampleCode = GetSampleCodeFormatted(RunNumber, SampleDate ?? default(DateTime), visitType, RunNumberType);
                    responseData.Results = new { FormattedSampleCode = formattedSampleCode, BarCodeNumber = LabBarCodeNum };
                    responseData.Status = "OK";
                }

                else if (reqType == "updateBillStatus" && billstatus != null)
                {
                    BillingDbContext billDbContext = new BillingDbContext(connString);
                    List<int> reqIds = DanpheJSONConvert.DeserializeObject<List<int>>(str);
                    foreach (var reqId in reqIds)
                    {
                        LabRequisitionModel dbrequisition = labDbContext.Requisitions
                                                        .Where(a => a.RequisitionId == reqId)
                                                        .FirstOrDefault<LabRequisitionModel>();

                        //VisitType could be changed in case of copy from earlier invoice.
                        BillingTransactionItemModel billTxnItem = (from item in billDbContext.BillingTransactionItems
                                                                   join srvDept in billDbContext.ServiceDepartment on item.ServiceDepartmentId equals srvDept.ServiceDepartmentId
                                                                   where srvDept.IntegrationName.ToLower() == "lab" && item.RequisitionId == reqId
                                                                   select item).OrderByDescending(a => a.BillingTransactionItemId).FirstOrDefault();
                        if (billTxnItem != null)
                        {
                            dbrequisition.VisitType = billTxnItem.VisitType;
                        }

                        dbrequisition.BillingStatus = billstatus;
                        dbrequisition.ModifiedBy = currentUser.EmployeeId;
                        dbrequisition.ModifiedOn = DateTime.Now;
                        labDbContext.Entry(dbrequisition).Property(a => a.BillingStatus).IsModified = true;
                        labDbContext.Entry(dbrequisition).Property(a => a.VisitType).IsModified = true;
                        labDbContext.Entry(dbrequisition).Property(a => a.ModifiedBy).IsModified = true;
                        labDbContext.Entry(dbrequisition).Property(a => a.ModifiedOn).IsModified = true;
                        //labDbContext.Entry(dbrequisition).State = EntityState.Modified;
                    }
                    labDbContext.SaveChanges();
                    responseData.Results = "lab Billing Status  updated successfully.";
                }


                else if (reqType == "UpdateCommentsOnTestRequisition")
                {
                    List<Int64> RequisitionIds = (DanpheJSONConvert.DeserializeObject<List<Int64>>(str));
                    //int newPrintId = printid + 1;
                    foreach (Int64 reqId in RequisitionIds)
                    {
                        List<LabRequisitionModel> listTestReq = labDbContext.Requisitions
                                                 .Where(a => a.RequisitionId == reqId)
                                                 .ToList<LabRequisitionModel>();
                        if (listTestReq != null)
                        {
                            foreach (var reqResult in listTestReq)
                            {
                                reqResult.Comments = comments;
                                //labDbContext.Entry(reqResult).State = EntityState.Modified;
                                labDbContext.Entry(reqResult).Property(a => a.Comments).IsModified = true;

                            }


                        }
                    }

                    labDbContext.SaveChanges();
                    responseData.Status = "OK";
                    responseData.Results = RequisitionIds;
                }

                // to update the lab result
                else if (reqType == "EditLabTestResult")
                {
                    string specimenDataModel = this.ReadQueryStringData("specimenData");
                    List<LabTestComponentResult> labtestsresults = DanpheJSONConvert.
                        DeserializeObject<List<LabTestComponentResult>>(str);
                    List<LabTestSpecimenModel> labSpecimenList = DanpheJSONConvert.DeserializeObject<List<LabTestSpecimenModel>>(specimenDataModel);
                    if (labtestsresults != null && labtestsresults.Count > 0)
                    {

                        var useNewMethod = true;//sud: use earlier method if this doesn't work correctly


                        if (useNewMethod)
                        {
                            EditComponentsResults(labDbContext, labtestsresults, currentUser);

                            //Update specimen of culture test
                            if (labSpecimenList != null && labSpecimenList.Count > 0)
                            {
                                int ln = labSpecimenList.Count;
                                for (int i = 0; i < ln; i++)
                                {
                                    int? requisitId = labSpecimenList[i].RequisitionId;
                                    string specimen = labSpecimenList[i].Specimen;
                                    if (requisitId != null && requisitId > 0)
                                    {
                                        LabRequisitionModel labReq = labDbContext.Requisitions.Where(val => val.RequisitionId == requisitId).FirstOrDefault<LabRequisitionModel>();
                                        labReq.LabTestSpecimen = specimen;
                                        labDbContext.SaveChanges();
                                    }
                                }
                            }

                            responseData.Status = "OK";
                            responseData.Results = new List<LabTestComponentResult>();
                        }
                        else
                        {
                            List<LabTestComponentResult> compsToUpdate = labtestsresults.Where(comp => comp.TestComponentResultId != 0).ToList();
                            List<LabTestComponentResult> compsToInsert = labtestsresults.Where(comp => comp.TestComponentResultId == 0).ToList();

                            var reportId = compsToInsert[0].LabReportId;

                            foreach (var labtestres in compsToUpdate)
                            {
                                LabTestComponentResult TestComp = labDbContext.LabTestComponentResults
                                                     .Where(a => a.TestComponentResultId == labtestres.TestComponentResultId)
                                                      .FirstOrDefault<LabTestComponentResult>();


                                TestComp.LabReportId = reportId;
                                TestComp.Value = labtestres.Value;
                                TestComp.Remarks = labtestres.Remarks;
                                TestComp.IsAbnormal = labtestres.IsAbnormal;
                                TestComp.AbnormalType = labtestres.AbnormalType;
                                TestComp.ModifiedOn = DateTime.Now;
                                TestComp.ModifiedBy = currentUser.EmployeeId;
                                labDbContext.Entry(TestComp).Property(a => a.LabReportId).IsModified = true;
                                labDbContext.Entry(TestComp).Property(a => a.Value).IsModified = true;
                                labDbContext.Entry(TestComp).Property(a => a.IsAbnormal).IsModified = true;
                                labDbContext.Entry(TestComp).Property(a => a.AbnormalType).IsModified = true;
                                labDbContext.Entry(TestComp).Property(a => a.ModifiedOn).IsModified = true;
                                labDbContext.Entry(TestComp).Property(a => a.ModifiedBy).IsModified = true;


                                //labDbContext.Entry(TestComp).State = EntityState.Modified;
                            }
                            labDbContext.SaveChanges();

                            //Add Extra added Components from FrontEnd Side
                            compsToInsert.ForEach(cmp =>
                            {
                                cmp.CreatedOn = DateTime.Now;
                                cmp.CreatedBy = currentUser.EmployeeId;
                                cmp.IsActive = true;
                                labDbContext.LabTestComponentResults.Add(cmp);

                                labtestsresults.Add(cmp);
                            });

                            labDbContext.SaveChanges();

                            responseData.Status = "OK";
                            responseData.Results = new List<LabTestComponentResult>();
                        }
                    }
                    else
                    {
                        responseData.Status = "Failed";
                        responseData.ErrorMessage = "Empty Component Sets";
                    }


                }
                else if (reqType == "update-labReport")
                {
                    LabReportModel clientReport = DanpheJSONConvert.DeserializeObject<LabReportModel>(str);
                    LabReportModel servReport = labDbContext.LabReports
                                             .Where(a => a.LabReportId == clientReport.LabReportId)
                                              .FirstOrDefault<LabReportModel>();

                    if (servReport != null)
                    {
                        servReport.ModifiedBy = currentUser.EmployeeId;
                        servReport.ModifiedOn = DateTime.Now;
                        servReport.Signatories = clientReport.Signatories;
                        servReport.Comments = clientReport.Comments;
                    }
                    labDbContext.Entry(servReport).Property(a => a.Signatories).IsModified = true;
                    labDbContext.Entry(servReport).Property(a => a.ModifiedOn).IsModified = true;
                    labDbContext.Entry(servReport).Property(a => a.ModifiedBy).IsModified = true;
                    labDbContext.Entry(servReport).Property(a => a.Comments).IsModified = true;
                    labDbContext.SaveChanges();

                    responseData.Status = "OK";
                }

                else if (reqType == "update-reportPrintedFlag")
                {
                    List<Int64> requisitionIdList = DanpheJSONConvert.DeserializeObject<List<Int64>>(labReqIdList);
                    int? repId = PrintedReportId;

                    List<int> reportIdList = new List<int>();

                    using (var dbContextTransaction = labDbContext.Database.BeginTransaction())
                    {
                        try
                        {
                            foreach (int req in requisitionIdList)
                            {
                                LabRequisitionModel labReq = labDbContext.Requisitions
                                                     .Where(a => a.RequisitionId == req)
                                                      .FirstOrDefault<LabRequisitionModel>();

                                labDbContext.Requisitions.Attach(labReq);
                                labDbContext.Entry(labReq).Property(a => a.PrintCount).IsModified = true;
                                labDbContext.Entry(labReq).Property(a => a.PrintedBy).IsModified = true;
                                if (labReq.PrintCount == null || labReq.PrintCount == 0)
                                {
                                    labReq.PrintCount = 1;
                                }
                                else { labReq.PrintCount = labReq.PrintCount + 1; }
                                labReq.PrintedBy = currentUser.EmployeeId;

                                if (labReq.LabReportId.HasValue)
                                {
                                    if (!reportIdList.Contains(labReq.LabReportId.Value))
                                    {
                                        reportIdList.Add(labReq.LabReportId.Value);
                                    }
                                }

                                labDbContext.SaveChanges();
                            }


                            if (reportIdList != null && reportIdList.Count > 0)
                            {
                                foreach (var repIdSelected in reportIdList)
                                {
                                    LabReportModel report = labDbContext.LabReports.Where(val => val.LabReportId == repIdSelected).FirstOrDefault<LabReportModel>();
                                    labDbContext.LabReports.Attach(report);

                                    labDbContext.Entry(report).Property(a => a.IsPrinted).IsModified = true;
                                    labDbContext.Entry(report).Property(a => a.PrintedOn).IsModified = true;
                                    labDbContext.Entry(report).Property(a => a.PrintedBy).IsModified = true;
                                    labDbContext.Entry(report).Property(a => a.PrintCount).IsModified = true;

                                    if (report.PrintCount == null)
                                    {
                                        report.PrintCount = 0;
                                    }

                                    report.IsPrinted = true;
                                    report.PrintedOn = System.DateTime.Now;
                                    report.PrintedBy = currentUser.EmployeeId;
                                    report.PrintCount = report.PrintCount + 1;

                                    labDbContext.SaveChanges();
                                    responseData.Results = report;
                                }
                                dbContextTransaction.Commit();
                            }
                            else
                            {
                                throw new Exception("Cannot find the report");
                            }

                            responseData.Status = "OK";
                        }
                        catch (Exception ex)
                        {
                            dbContextTransaction.Rollback();
                            throw (ex);
                        }
                    }


                }

                else if (reqType == "UpdateDoctor")
                {
                    //update doctor name for here.. 
                    List<Int32> reqList = DanpheJSONConvert.DeserializeObject<List<Int32>>(str);

                    int reffByDocId = referredById;
                    string refferedByDoctorName = (from emp in labDbContext.Employee
                                                   where emp.EmployeeId == reffByDocId
                                                   select emp.LongSignature
                                                   ).FirstOrDefault<string>();

                    foreach (int req in reqList)
                    {
                        LabRequisitionModel labReq = labDbContext.Requisitions
                                             .Where(a => a.RequisitionId == req)
                                              .FirstOrDefault<LabRequisitionModel>();

                        labDbContext.Requisitions.Attach(labReq);

                        labDbContext.Entry(labReq).Property(a => a.ProviderId).IsModified = true;
                        labDbContext.Entry(labReq).Property(a => a.ProviderName).IsModified = true;
                        labDbContext.Entry(labReq).Property(a => a.ModifiedOn).IsModified = true;
                        labDbContext.Entry(labReq).Property(a => a.ModifiedBy).IsModified = true;

                        labReq.ProviderName = refferedByDoctorName;
                        labReq.ProviderId = reffByDocId;
                        labReq.ModifiedBy = currentUser.EmployeeId;
                        labReq.ModifiedOn = DateTime.Now;
                        labDbContext.SaveChanges();
                    }

                    responseData.Status = "OK";

                }
                else if (reqType == "UpdateDoctorNameInLabReport")
                {
                    int id = Convert.ToInt32(this.ReadQueryStringData("id"));

                    LabReportModel labreport = labDbContext.LabReports
                        .Where(rep => rep.LabReportId == id).FirstOrDefault<LabReportModel>();

                    labDbContext.LabReports.Attach(labreport);

                    labDbContext.Entry(labreport).Property(a => a.ReferredByDr).IsModified = true;
                    labDbContext.Entry(labreport).Property(a => a.ModifiedOn).IsModified = true;
                    labDbContext.Entry(labreport).Property(a => a.ModifiedBy).IsModified = true;

                    labreport.ReferredByDr = str;
                    labreport.ModifiedBy = currentUser.EmployeeId;
                    labreport.ModifiedOn = DateTime.Now;
                    labDbContext.SaveChanges();

                    responseData.Status = "OK";
                }
                else if (reqType == "ChangeLabTestWithSamePrice")
                {

                    using (var dbContextTransaction = labDbContext.Database.BeginTransaction())
                    {
                        try
                        {
                            int reqId = Convert.ToInt32(this.ReadQueryStringData("requisitionid"));
                            //var ChangedLabTest = JsonConvert.DeserializeAnonymousType(str, BillItemVM);
                            LabTestTransactionItemVM ChangedLabTest = DanpheJSONConvert.DeserializeObject<LabTestTransactionItemVM>(str);

                            var labServiceDeptList = (from dpt in labDbContext.Department
                                                      join serviceDept in labDbContext.ServiceDepartment on dpt.DepartmentId equals serviceDept.DepartmentId
                                                      where dpt.DepartmentName.ToLower() == "lab"
                                                      select serviceDept.ServiceDepartmentId).ToList();

                            BillingTransactionItemModel itemTransaction = (from billItem in labDbContext.BillingTransactionItems
                                                                           where billItem.RequisitionId == reqId && labServiceDeptList.Contains(billItem.ServiceDepartmentId)
                                                                           select billItem).FirstOrDefault<BillingTransactionItemModel>();

                            labDbContext.BillingTransactionItems.Attach(itemTransaction);
                            labDbContext.Entry(itemTransaction).Property(a => a.ItemId).IsModified = true;
                            labDbContext.Entry(itemTransaction).Property(a => a.ItemName).IsModified = true;
                            labDbContext.Entry(itemTransaction).Property(a => a.ServiceDepartmentId).IsModified = true;
                            labDbContext.Entry(itemTransaction).Property(a => a.ServiceDepartmentName).IsModified = true;

                            itemTransaction.ItemId = ChangedLabTest.ItemId;
                            itemTransaction.ItemName = ChangedLabTest.ItemName;
                            itemTransaction.ServiceDepartmentId = ChangedLabTest.ServiceDepartmentId;
                            itemTransaction.ServiceDepartmentName = ChangedLabTest.ServiceDepartmentName;

                            labDbContext.SaveChanges();

                            LabRequisitionModel labReq = labDbContext.Requisitions
                                                        .Where(val => val.RequisitionId == reqId)
                                                        .FirstOrDefault<LabRequisitionModel>();

                            LabTestModel labTest = labDbContext.LabTests.
                                                   Where(val => val.LabTestId == ChangedLabTest.ItemId)
                                                   .FirstOrDefault<LabTestModel>();

                            LabReportTemplateModel defRptTempModel = labDbContext.LabReportTemplates.
                                                        Where(val => val.IsDefault == true)
                                                        .FirstOrDefault();

                            labDbContext.Requisitions.Attach(labReq);
                            labDbContext.Entry(labReq).Property(a => a.LabTestId).IsModified = true;
                            labDbContext.Entry(labReq).Property(a => a.LabTestName).IsModified = true;
                            labDbContext.Entry(labReq).Property(a => a.ReportTemplateId).IsModified = true;
                            labDbContext.Entry(labReq).Property(a => a.RunNumberType).IsModified = true;


                            labReq.LabTestName = ChangedLabTest.ItemName;
                            labReq.LabTestId = ChangedLabTest.ItemId;
                            labReq.RunNumberType = labTest.RunNumberType;

                            int newRptTempId = 1;//hardcoded value

                            if (defRptTempModel != null)
                            {
                                newRptTempId = defRptTempModel.ReportTemplateID;
                            }
                            labReq.ReportTemplateId = labTest.ReportTemplateId.HasValue ? (int)labTest.ReportTemplateId : newRptTempId;

                            labDbContext.SaveChanges();
                            dbContextTransaction.Commit();

                            responseData.Status = "OK";
                            responseData.Results = labTest;


                        }
                        catch (Exception ex)
                        {
                            dbContextTransaction.Rollback();
                            throw (ex);
                        }
                    }
                }

                else if (reqType == "cancelInpatientLabTest")
                {

                    using (var labDbContextTransaction = labDbContext.Database.BeginTransaction())
                    {
                        try
                        {
                            InpatientLabTestModel inpatientLabTest = DanpheJSONConvert.DeserializeObject<InpatientLabTestModel>(str);



                            BillingTransactionItemModel billItem = labDbContext.BillingTransactionItems
                                                                    .Where(itm =>
                                                                            itm.RequisitionId == inpatientLabTest.RequisitionId
                                                                            && itm.ItemId == inpatientLabTest.LabTestId
                                                                            && itm.PatientId == inpatientLabTest.PatientId
                                                                            && itm.PatientVisitId == inpatientLabTest.PatientVisitId
                                                                            && (itm.BillingType.ToLower() == ENUM_BillingType.inpatient
                                                                            || itm.BillingType.ToLower() == ENUM_BillingType.outpatient)// "inpatient", "outpatient" for cancellation from er
                                                                            && itm.BillStatus.ToLower() != ENUM_BillingStatus.paid // "paid"
                                                                            && itm.BillingTransactionItemId == inpatientLabTest.BillingTransactionItemId
                                                                        ).FirstOrDefault<BillingTransactionItemModel>();


                            labDbContext.BillingTransactionItems.Attach(billItem);

                            labDbContext.Entry(billItem).Property(a => a.BillStatus).IsModified = true;
                            labDbContext.Entry(billItem).Property(a => a.CancelledBy).IsModified = true;
                            labDbContext.Entry(billItem).Property(a => a.CancelledOn).IsModified = true;
                            labDbContext.Entry(billItem).Property(a => a.CancelRemarks).IsModified = true;

                            billItem.BillStatus = ENUM_BillingStatus.cancel;// "cancel";
                            billItem.CancelledBy = currentUser.EmployeeId;
                            billItem.CancelledOn = System.DateTime.Now;
                            billItem.CancelRemarks = inpatientLabTest.CancelRemarks;
                            labDbContext.SaveChanges();



                            LabRequisitionModel labReq = labDbContext.Requisitions
                                                            .Where(req => req.RequisitionId == inpatientLabTest.RequisitionId
                                                                && (
                                                                req.VisitType.ToLower() == ENUM_VisitType.inpatient // "inpatient"
                                                                || req.VisitType.ToLower() == ENUM_VisitType.emergency //"emergency"
                                                                )
                                                                && req.BillingStatus.ToLower() != ENUM_BillingStatus.paid // "paid"
                                                            ).FirstOrDefault<LabRequisitionModel>();

                            labReq.BillCancelledBy = currentUser.EmployeeId;
                            labReq.BillCancelledOn = System.DateTime.Now;
                            labDbContext.Requisitions.Attach(labReq);

                            labDbContext.Entry(labReq).Property(a => a.BillingStatus).IsModified = true;
                            labDbContext.Entry(labReq).Property(a => a.BillCancelledBy).IsModified = true;
                            labDbContext.Entry(labReq).Property(a => a.BillCancelledOn).IsModified = true;
                            labReq.BillingStatus = ENUM_BillingStatus.cancel;// "cancel";
                            labDbContext.SaveChanges();

                            labDbContextTransaction.Commit();

                            responseData.Status = "OK";
                            responseData.Results = null;

                        }
                        catch (Exception ex)
                        {
                            labDbContextTransaction.Rollback();
                            throw (ex);
                        }
                    }
                }

                else if (reqType == "update-specimen")
                {
                    int reqId = Convert.ToInt32(this.ReadQueryStringData("ReqId"));
                    string specimen = this.ReadQueryStringData("Specimen");
                    if (reqId > 0)
                    {
                        LabRequisitionModel labReq = labDbContext.Requisitions.Where(val => val.RequisitionId == reqId).FirstOrDefault<LabRequisitionModel>();
                        labReq.LabTestSpecimen = specimen;
                        labReq.ModifiedBy = currentUser.EmployeeId;
                        labReq.ModifiedOn = DateTime.Now;
                        labDbContext.Entry(labReq).Property(a => a.LabTestSpecimen).IsModified = true;
                        labDbContext.Entry(labReq).Property(a => a.ModifiedBy).IsModified = true;
                        labDbContext.Entry(labReq).Property(a => a.ModifiedOn).IsModified = true;
                        labDbContext.SaveChanges();
                    }
                    responseData.Status = "OK";
                    responseData.Results = specimen;
                }

                else if (reqType == "undo-samplecode")
                {
                    List<Int64> RequisitionIds = (DanpheJSONConvert.DeserializeObject<List<Int64>>(str));

                    try
                    {
                        using (var trans = new TransactionScope())
                        {
                            //int newPrintId = printid + 1;
                            foreach (Int64 reqId in RequisitionIds)
                            {
                                List<LabRequisitionModel> listTestReq = labDbContext.Requisitions
                                                         .Where(a => a.RequisitionId == reqId)
                                                         .ToList<LabRequisitionModel>();
                                if (listTestReq != null)
                                {
                                    foreach (var reqResult in listTestReq)
                                    {
                                        reqResult.SampleCode = null;
                                        reqResult.SampleCreatedBy = null;
                                        reqResult.SampleCreatedOn = null;
                                        reqResult.OrderStatus = ENUM_LabOrderStatus.Active; //"active";
                                        reqResult.ModifiedBy = currentUser.EmployeeId;
                                        reqResult.ModifiedOn = DateTime.Now;
                                        reqResult.LabTestSpecimen = null;
                                        reqResult.LabTestSpecimenSource = null;
                                        reqResult.BarCodeNumber = null;

                                        labDbContext.Entry(reqResult).Property(a => a.SampleCode).IsModified = true;
                                        labDbContext.Entry(reqResult).Property(a => a.SampleCreatedBy).IsModified = true;
                                        labDbContext.Entry(reqResult).Property(a => a.SampleCreatedOn).IsModified = true;
                                        labDbContext.Entry(reqResult).Property(a => a.OrderStatus).IsModified = true;
                                        labDbContext.Entry(reqResult).Property(a => a.ModifiedBy).IsModified = true;
                                        labDbContext.Entry(reqResult).Property(a => a.ModifiedOn).IsModified = true;
                                        labDbContext.Entry(reqResult).Property(a => a.LabTestSpecimen).IsModified = true;
                                        labDbContext.Entry(reqResult).Property(a => a.LabTestSpecimenSource).IsModified = true;
                                        labDbContext.Entry(reqResult).Property(a => a.BarCodeNumber).IsModified = true;

                                    }


                                }
                            }
                            labDbContext.SaveChanges();

                            string reqIdList = string.Join(",", RequisitionIds);
                            List<SqlParameter> paramList = new List<SqlParameter>(){
                                                    new SqlParameter("@allReqIds", reqIdList),
                                                    new SqlParameter("@status", ENUM_BillingOrderStatus.Active)
                                                };
                            DataTable statusUpdated = DALFunctions.GetDataTableFromStoredProc("SP_Bill_OrderStatusUpdate", paramList, labDbContext);

                            trans.Complete();

                            responseData.Status = "OK";
                            responseData.Results = RequisitionIds;
                        }
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }
                }

                else if (reqType == "verify-all-labtests")
                {
                    LabReportModel labReport = DanpheJSONConvert.DeserializeObject<LabReportModel>(str);
                    var VerificationEnabled = labReport.VerificationEnabled;


                    List<Int64> reqIdList = new List<Int64>();

                    using (var verifyTransaction = labDbContext.Database.BeginTransaction())
                    {
                        try
                        {
                            if (VerificationEnabled != null && VerificationEnabled == true)
                            {
                                if (labReport.LabReportId != 0)
                                {
                                    var report = labDbContext.LabReports.Where(r => r.LabReportId == labReport.LabReportId).FirstOrDefault();
                                    if (report != null)
                                    {
                                        report.Signatories = labReport.Signatories;
                                    }
                                    labDbContext.Entry(report).Property(r => r.Signatories).IsModified = true;
                                    labDbContext.SaveChanges();


                                    foreach (var componentId in labReport.ComponentIdList)
                                    {
                                        LabTestComponentResult component = labDbContext.LabTestComponentResults.Where(cmp => cmp.TestComponentResultId == componentId).FirstOrDefault();
                                        reqIdList.Add(component.RequisitionId);
                                    }


                                    var reqIdToUpdate = reqIdList.Distinct().ToList();

                                    foreach (var reqId in reqIdToUpdate)
                                    {
                                        LabRequisitionModel requisitionItem = labDbContext.Requisitions.Where(val => val.RequisitionId == reqId).FirstOrDefault();

                                        requisitionItem.OrderStatus = ENUM_LabOrderStatus.ReportGenerated; //"report-generated";
                                        requisitionItem.VerifiedBy = currentUser.EmployeeId;
                                        requisitionItem.VerifiedOn = DateTime.Now;
                                        requisitionItem.IsVerified = true;

                                        labDbContext.Entry(requisitionItem).Property(a => a.OrderStatus).IsModified = true;
                                        labDbContext.Entry(requisitionItem).Property(a => a.VerifiedBy).IsModified = true;
                                        labDbContext.Entry(requisitionItem).Property(a => a.VerifiedOn).IsModified = true;
                                        labDbContext.Entry(requisitionItem).Property(a => a.IsVerified).IsModified = true;

                                        labDbContext.SaveChanges();
                                    }

                                    verifyTransaction.Commit();

                                }

                            }
                        }
                        catch (Exception ex)
                        {
                            verifyTransaction.Rollback();
                            throw ex;
                        }
                    }

                    responseData.Status = "OK";
                    responseData.Results = labReport;
                }

                else if (reqType == "verify-all-requisitions-directly")
                {
                    List<Int64> reqIdList = DanpheJSONConvert.DeserializeObject<List<Int64>>(str);

                    foreach (var reqId in reqIdList)
                    {
                        LabRequisitionModel requisitionItem = labDbContext.Requisitions.Where(val => val.RequisitionId == reqId).FirstOrDefault();

                        requisitionItem.OrderStatus = ENUM_LabOrderStatus.ReportGenerated; //"report-generated";
                        requisitionItem.VerifiedBy = currentUser.EmployeeId;
                        requisitionItem.VerifiedOn = DateTime.Now;
                        requisitionItem.IsVerified = true;

                        labDbContext.Entry(requisitionItem).Property(a => a.OrderStatus).IsModified = true;
                        labDbContext.Entry(requisitionItem).Property(a => a.VerifiedBy).IsModified = true;
                        labDbContext.Entry(requisitionItem).Property(a => a.VerifiedOn).IsModified = true;
                        labDbContext.Entry(requisitionItem).Property(a => a.IsVerified).IsModified = true;

                        labDbContext.SaveChanges();
                    }

                    responseData.Status = "OK";
                }

                else if (reqType == "UpdateVendorIdToLabTestRequisition")
                {
                    var newVendorId = vendorId;
                    List<Int64> RequisitionIds = (DanpheJSONConvert.DeserializeObject<List<Int64>>(str));
                    foreach (Int64 reqId in RequisitionIds)
                    {
                        LabRequisitionModel singleRequisition = labDbContext.Requisitions
                                                  .Where(a => a.RequisitionId == reqId)
                                                  .FirstOrDefault();
                        if (singleRequisition != null)
                        {
                            singleRequisition.ResultingVendorId = newVendorId;
                            labDbContext.Entry(singleRequisition).Property(a => a.ResultingVendorId).IsModified = true;
                            labDbContext.SaveChanges();
                        }
                    }

                    responseData.Status = "OK";
                }

                else if (reqType == "SampleCodeFormatted")
                {
                    List<LabRequisitionModel> allLabRequisitions = (from req in labDbContext.Requisitions
                                                                    where req.SampleCreatedOn.HasValue && req.SampleCode.HasValue
                                                                    select req).ToList();
                    foreach (var item in allLabRequisitions)
                    {
                        item.SampleCodeFormatted = GetSampleCodeFormatted(item.SampleCode.Value, item.SampleCreatedOn.Value, item.VisitType.ToLower(), item.RunNumberType.ToLower());

                        labDbContext.Entry(item).Property(a => a.SampleCodeFormatted).IsModified = true;
                    }
                    labDbContext.SaveChanges();

                }
                else if (reqType == "transfertoLab")
                {
                    var requisitionId = Convert.ToInt32(this.ReadQueryStringData("reqId"));
                    var labType = this.ReadQueryStringData("labTypeName");

                    LabRequisitionModel labRequest = (from req in labDbContext.Requisitions
                                                      where req.RequisitionId == requisitionId
                                                      select req).FirstOrDefault();
                    BillingTransactionItemModel billingItem = (from bil in labDbContext.BillingTransactionItems
                                                               where bil.RequisitionId == requisitionId
                                                               select bil).FirstOrDefault();

                    using (var dbContextTransaction = labDbContext.Database.BeginTransaction())
                    {
                        try
                        {
                            labRequest.LabTypeName = labType;
                            labDbContext.Entry(labRequest).Property(a => a.LabTypeName).IsModified = true;
                            billingItem.LabTypeName = labType;
                            labDbContext.Entry(billingItem).Property(a => a.LabTypeName).IsModified = true;

                            labDbContext.SaveChanges();
                            dbContextTransaction.Commit();
                            responseData.Status = "OK";
                            responseData.Results = labRequest;
                        }
                        catch (Exception ex)
                        {
                            dbContextTransaction.Rollback();
                            throw ex;
                        }
                    }

                }

                else
                {
                    responseData.Status = "Failed";
                    responseData.ErrorMessage = "Invalid request type.";
                }

                #region //Commented codes. Delete ASAP
                // updating Printstatus on TestComponent result table
                //else if (reqType == "UpdatePrintStatusForReport")
                //{
                //    List<Int64> RequisitionIds = (DanpheJSONConvert.DeserializeObject<List<Int64>>(str));
                //    foreach (Int64 reqId in RequisitionIds)
                //    {
                //        List<LabTestComponentResult> listTestComp = labDbContext.LabTestComponents
                //                                 .Where(a => a.RequisitionId == reqId)
                //                                 .ToList<LabTestComponentResult>();
                //        if (listTestComp != null)
                //        {
                //            foreach (var compResult in listTestComp)
                //            {
                //                compResult.IsPrint = true;
                //                compResult.LabReportId = printid;
                //                labDbContext.Entry(compResult).Property(a => a.IsPrint).IsModified = true;
                //                labDbContext.Entry(compResult).Property(a => a.LabReportId).IsModified = true;
                //                //labDbContext.Entry(compResult).State = EntityState.Modified;
                //            }
                //        }
                //    }
                //    labDbContext.SaveChanges();
                //    responseData.Status = "OK";
                //    responseData.Results = RequisitionIds;
                //}

                //updating doctors remark
                //else if (reqType != null && reqType == "AddDoctorsRemark")
                //{

                //    List<LabTestComponentResult> labtestresults = DanpheJSONConvert.DeserializeObject<List<LabTestComponentResult>>(str); ;

                //    foreach (var labtestresult in labtestresults)
                //    {

                //        LabTestComponentResult dbresult = labDbContext.LabResults
                //                                        .Where(a => a.RequisitionId == labtestresult.RequisitionId)
                //                                        .FirstOrDefault<LabTestComponentResult>();
                //        dbresult.DoctorsRemark = labtestresult.DoctorsRemark;

                //        labDbContext.Entry(dbresult).State = EntityState.Modified;
                //    }
                //    labDbContext.SaveChanges();
                //    responseData.Results = "lab Doctors Remark  updated successfully.";
                //}

                ////updating labOrderStatus
                //else
                //{

                //    List<LabTestComponentResult> labtestsresults = DanpheJSONConvert.
                //        DeserializeObject<List<LabTestComponentResult>>(str);

                //    foreach (var labtestresult in labtestsresults)
                //    {
                //        LabRequisitionModel dbRequisition = labDbContext.Requisitions
                //                                        .Where(a => a.RequisitionId == labtestresult.RequisitionId)
                //                                        .FirstOrDefault<LabRequisitionModel>();

                //        dbRequisition.OrderStatus = "final";
                //        labDbContext.Entry(dbRequisition).State = EntityState.Modified;
                //    }
                //    labDbContext.SaveChanges();
                //    responseData.Results = "lab Order Status  updated successfully.";
                //}
                #endregion
            }
            catch (Exception ex)
            {
                responseData.Status = "Failed";
                responseData.ErrorMessage = ex.Message + " exception details:" + ex.ToString();
            }
            return DanpheJSONConvert.SerializeObject(responseData, true);
        }

        // DELETE api/values/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }

        private LabSMSModel GetSmsMessageAndNumberOfPatientByReqId(LabDbContext labDbContext, long reqId)
        {
            LabSMSModel data = new LabSMSModel();
            var patientData = (from req in labDbContext.Requisitions
                               join test in labDbContext.LabTests on req.LabTestId equals test.LabTestId
                               join pat in labDbContext.Patients on req.PatientId equals pat.PatientId
                               join txn in labDbContext.LabTestComponentResults on req.RequisitionId equals txn.RequisitionId
                               where req.RequisitionId == reqId //&& test.LabTestName == "idsList"
                               select new
                               {
                                   RetuisitionId = req.RequisitionId,
                                   TestName = test.LabTestName,
                                   PatientName = pat.ShortName,
                                   PhoneNumber = pat.PhoneNumber,
                                   Result = txn.Value,
                                   SampleCollectedOn = req.SampleCollectedOnDateTime.Value,
                                   CovidFileId = req.GoogleFileIdForCovid
                               }).FirstOrDefault();
            if (patientData != null)
            {
                var fileUrl = CovidReportUrlComonPath.Replace("GGLFILEUPLOADID", patientData.CovidFileId);
                data.Message = "Dear " + patientData.PatientName + ", " + "Your COVID-19 Report is " + patientData.Result + "." + "\n" + "Sample collected on " + patientData.SampleCollectedOn.ToString("M/d/yyyy")
                    + (!string.IsNullOrWhiteSpace(patientData.CovidFileId) ? ("\n" + fileUrl) : "");
                data.PhoneNumber = patientData.PhoneNumber;
                data.RequisitionId = reqId;
            }
            return data;
        }

        private bool PutOrderStatusOfRequisitions(LabDbContext labDbContext, List<Int64> requisitionIds, string orderStatus)
        {
            foreach (Int64 reqId in requisitionIds)
            {
                LabRequisitionModel dbRequisition = labDbContext.Requisitions
                                                .Where(a => a.RequisitionId == reqId)
                                                .FirstOrDefault<LabRequisitionModel>();

                if (dbRequisition != null)
                {
                    dbRequisition.OrderStatus = orderStatus;
                    labDbContext.Entry(dbRequisition).Property(a => a.OrderStatus).IsModified = true;
                }
            }
            labDbContext.SaveChanges();

            return true;

        }

        private int? GetInpatientLatestSampleSequence(LabDbContext labDbContext)
        {
            int? newSampleSequence = 1;

            var samplesByType = (from req in labDbContext.Requisitions
                                 where (req.VisitType.ToLower() == ENUM_VisitType.inpatient // "inpatient" 
                                         || req.VisitType.ToLower() == ENUM_VisitType.emergency) // "emergency") 
                                 && (req.RunNumberType.ToLower() != ENUM_LabRunNumType.cyto) // "cyto") 
                                 && (req.RunNumberType.ToLower() != ENUM_LabRunNumType.histo) // "histo")
                                 && (req.SampleCode.HasValue)
                                 select new
                                 {
                                     RequisitionId = req.RequisitionId,
                                     SampleDate = req.SampleCreatedOn,
                                     SampleCode = req.SampleCode
                                 }).ToList();


            var latestYearSampleCode = (from smpl in samplesByType
                                        where DanpheDateConvertor.ConvertEngToNepDate((DateTime)smpl.SampleDate).Year
                                            == DanpheDateConvertor.ConvertEngToNepDate(System.DateTime.Now).Year
                                        group smpl by 1 into req
                                        select new
                                        {
                                            SampleCode = req.Max(a => a.SampleCode)
                                        }).FirstOrDefault();

            if (latestYearSampleCode != null)
            {
                newSampleSequence = (int)latestYearSampleCode.SampleCode + 1;
            }

            return newSampleSequence;

        }


        private int? GetLatestSampleSequence(List<LabRequisitionModel> allReqOfCurrentType,
            LabRunNumberSettingsModel currentSetting, DateTime currentSampleDate)
        {
            int? newSampleSequence = 0;

            List<int> allMaxSampleCodesForEachType = new List<int>();

            var allReqFilteredByCurrYear = (from smpl in allReqOfCurrentType
                                            where DanpheDateConvertor.ConvertEngToNepDate((DateTime)smpl.SampleCreatedOn).Year
                                                == DanpheDateConvertor.ConvertEngToNepDate(currentSampleDate).Year
                                            select smpl).ToList();

            DateTime? currentDateTime = currentSampleDate.Date;


            //currentSetting.ResetMonthly ? (req.SampleCreatedOn.Value.Month == SampleDate.Date.Month) : true
            //                               && currentSetting.ResetDaily ? ((req.SampleCreatedOn.Value.Month == SampleDate.Month)
            //                               && (req.SampleCreatedOn.Value.Day == SampleDate.Day)) : true

            //If the Reset if Yearly
            if (currentSetting.ResetYearly)
            {
                var latestYearSampleCode = (from smpl in allReqFilteredByCurrYear
                                            group smpl by 1 into req
                                            select new
                                            {
                                                SampleCode = req.Max(a => a.SampleCode)
                                            }).FirstOrDefault();

                if (latestYearSampleCode != null)
                {
                    var maxCodeForThisType = (int)latestYearSampleCode.SampleCode;
                    allMaxSampleCodesForEachType.Add(maxCodeForThisType);
                }
            }
            //If the Reset is Daily
            else if (currentSetting.ResetDaily)
            {
                var latestSampleCodeForThisType = (from smpl in allReqFilteredByCurrYear
                                                   where smpl.SampleCreatedOn.Value.Date == currentDateTime.Value.Date
                                                   group smpl by 1 into req
                                                   select new
                                                   {
                                                       SampleCode = req.Max(a => a.SampleCode)
                                                   }).FirstOrDefault();

                if (latestSampleCodeForThisType != null)
                {
                    var maxCodeForThisType = (int)latestSampleCodeForThisType.SampleCode;
                    allMaxSampleCodesForEachType.Add(maxCodeForThisType);
                }
            }
            //If the Reset is Monthly
            else if (currentSetting.ResetMonthly)
            {
                var latestYearSampleCode = (from smpl in allReqFilteredByCurrYear
                                            where DanpheDateConvertor.ConvertEngToNepDate((DateTime)smpl.SampleCreatedOn).Month
                                                == DanpheDateConvertor.ConvertEngToNepDate(System.DateTime.Now).Month
                                            group smpl by 1 into req
                                            select new
                                            {
                                                SampleCode = req.Max(a => a.SampleCode)
                                            }).FirstOrDefault();

                if (latestYearSampleCode != null)
                {
                    var maxCodeForThisType = (int)latestYearSampleCode.SampleCode;
                    allMaxSampleCodesForEachType.Add(maxCodeForThisType);
                }

            }


            if (allMaxSampleCodesForEachType.Count > 0)
            {
                newSampleSequence = allMaxSampleCodesForEachType.Max();
            }

            newSampleSequence = newSampleSequence + 1;
            return newSampleSequence;
        }

        //Anish: 30 Aug 2019 : SampleCode Logic Rendered from Format setting table present in the Cache
        public string GetSampleCodeFormatted(int? sampleCode, DateTime sampleCreatedOn,
            string visitType, string RunNumberType, bool isUnderInsurance = false)
        {
            visitType = visitType.ToLower();
            RunNumberType = RunNumberType.ToLower();

            //List<LabRunNumberSettingsModel> allLabRunNumberSettings = (List<LabRunNumberSettingsModel>)DanpheCache.GetMasterData(MasterDataEnum.LabRunNumberSettings);
            LabRunNumberSettingsModel currentRunNumSetting = LabRunNumberSettings.Where(st => st.RunNumberType == RunNumberType
            && st.VisitType == visitType && st.UnderInsurance == isUnderInsurance).FirstOrDefault();


            if (currentRunNumSetting != null && sampleCode != null)
            {
                var sampleLetter = currentRunNumSetting.StartingLetter;

                if (String.IsNullOrWhiteSpace(sampleLetter))
                {
                    sampleLetter = "";
                }

                var beforeSeparator = currentRunNumSetting.FormatInitialPart;
                var separator = currentRunNumSetting.FormatSeparator;
                var afterSeparator = currentRunNumSetting.FormatLastPart;

                if (beforeSeparator == "num")
                {
                    if (afterSeparator.Contains("yy"))
                    {
                        var afterSeparatorLength = afterSeparator.Length;
                        NepaliDateType nepDate = DanpheDateConvertor.ConvertEngToNepDate(sampleCreatedOn);
                        return sampleLetter + sampleCode.ToString() + separator + nepDate.Year.ToString().Substring(1, afterSeparatorLength);
                    }
                    else if (afterSeparator.Contains("dd"))
                    {
                        NepaliDateType nepDate = DanpheDateConvertor.ConvertEngToNepDate(sampleCreatedOn);
                        return sampleLetter + sampleCode.ToString() + separator + nepDate.Day.ToString();
                    }
                    else if (afterSeparator.Contains("mm"))
                    {
                        NepaliDateType nepDate = DanpheDateConvertor.ConvertEngToNepDate(sampleCreatedOn);
                        return sampleLetter + sampleCode.ToString() + separator + nepDate.Month.ToString();
                    }
                    else
                    {
                        return sampleLetter + sampleCode;
                    }
                }
                else
                {
                    if (beforeSeparator.Contains("yy"))
                    {
                        var beforeSeparatorLength = beforeSeparator.Length;
                        NepaliDateType nepDate = DanpheDateConvertor.ConvertEngToNepDate(sampleCreatedOn);
                        return sampleLetter + nepDate.Year.ToString().Substring(1, beforeSeparatorLength) + separator + sampleCode.ToString();
                    }
                    else if (beforeSeparator.Contains("dd"))
                    {
                        NepaliDateType nepDate = DanpheDateConvertor.ConvertEngToNepDate(sampleCreatedOn);
                        return sampleLetter + nepDate.Day.ToString() + separator + sampleCode.ToString();
                    }
                    else if (beforeSeparator.Contains("mm"))
                    {
                        NepaliDateType nepDate = DanpheDateConvertor.ConvertEngToNepDate(sampleCreatedOn);
                        return sampleLetter + nepDate.Month.ToString() + separator + sampleCode.ToString();
                    }
                    else
                    {
                        return sampleLetter + sampleCode;
                    }
                }


            }
            else
            {
                throw new ArgumentException("Cannot Get Samplecode. Didnt Found Any Format");
            }
        }

        //Suraj:06Sep2018 ReportTemplateId could be updated in case of html template.
        public void UpdateReportTemplateId(Int64 requisitionid, int? templateId, LabDbContext labDbContext, int currentUserId)
        {

            LabRequisitionModel labRequisition = (from req in labDbContext.Requisitions
                                                  where req.RequisitionId == requisitionid
                                                  select req).FirstOrDefault();
            labRequisition.ReportTemplateId = templateId ?? default(int);
            labRequisition.ModifiedBy = currentUserId;
            labRequisition.ModifiedOn = DateTime.Now;
            labDbContext.Entry(labRequisition).Property(a => a.ReportTemplateId).IsModified = true;
            labDbContext.Entry(labRequisition).Property(a => a.ModifiedBy).IsModified = true;
            labDbContext.Entry(labRequisition).Property(a => a.ModifiedOn).IsModified = true;
            labDbContext.SaveChanges();
        }

        //sud: 19Sept'18
        private void EditComponentsResults(LabDbContext dbContext, List<LabTestComponentResult> compsList, RbacUser currentUser)
        {
            if (compsList != null && compsList.Count > 0)
            {
                //to update Report Template ID on Requisition Table                
                Int64 reqId = compsList[0].RequisitionId;
                int? templateId = compsList[0].TemplateId;

                LabRequisitionModel LabRequisition = dbContext.Requisitions.Where(val => val.RequisitionId == reqId).FirstOrDefault();
                if (LabRequisition.ReportTemplateId != reqId)
                {
                    UpdateReportTemplateId(reqId, templateId, dbContext, currentUser.EmployeeId);
                }
                //Template ID Updated in Requisition Table


                //get distinct requisitionids, where we need to update or add components.
                List<Int64> distinctReqs = compsList.Select(c => c.RequisitionId).Distinct().ToList();



                foreach (var req in distinctReqs)
                {

                    //Section-1: Get/Filter All Components of Current Requisition from Databasea as well as from client.
                    List<LabTestComponentResult> requisitionsComps_Db = dbContext.LabTestComponentResults.Where(c => c.RequisitionId == req
                    && (c.IsActive.HasValue ? c.IsActive == true : true)).ToList();

                    List<LabTestComponentResult> reqsComps_Client = compsList.Where(c => c.RequisitionId == req).ToList();


                    //Section-2: Separate Client components in two groups: a. Newly Added, b. Updated
                    List<LabTestComponentResult> newlyAddedComps_Client = reqsComps_Client.Where(c => c.TestComponentResultId == 0).ToList();
                    List<LabTestComponentResult> updatedComps_Client = reqsComps_Client.Where(c => c.TestComponentResultId != 0).ToList();

                    ///Section-3: Create list of deleted components by checking if Component existing in db has come or not from Client.
                    List<LabTestComponentResult> deletedComps_Db = new List<LabTestComponentResult>();
                    //find better approach to get deleted components
                    foreach (var dbComp in requisitionsComps_Db)
                    {
                        //if component from db is not found in client's list then delete it. i.e: set IsActive=0
                        if (reqsComps_Client.Where(c => c.TestComponentResultId == dbComp.TestComponentResultId).Count() == 0)
                        {
                            deletedComps_Db.Add(dbComp);
                        }
                    }

                    //Section-4: Add new components to dbContext -- Don't save it yet, we'll do the Save action at the end.
                    if (newlyAddedComps_Client.Count > 0)
                    {
                        foreach (var c in newlyAddedComps_Client)
                        {
                            c.CreatedBy = currentUser.EmployeeId;
                            dbContext.LabTestComponentResults.Add(c);
                        }
                    }

                    //Section:5--Update values of dbComponent by that of ClientComponent--
                    //note that we have to update the peoperties of dbComponent, not ClientComponent.
                    //DON'T Save it YET.. we'll do that at the end..
                    if (updatedComps_Client.Count > 0)
                    {
                        foreach (var comp in updatedComps_Client)
                        {
                            LabTestComponentResult dbComp = requisitionsComps_Db.Where(c => c.TestComponentResultId == comp.TestComponentResultId).FirstOrDefault();

                            if ((dbComp.Value != comp.Value) || (dbComp.Range != comp.Range) || (dbComp.RangeDescription != comp.RangeDescription)
                                || (dbComp.Remarks != comp.Remarks) || (dbComp.IsNegativeResult != comp.IsNegativeResult))
                            {
                                dbComp.ModifiedOn = DateTime.Now;
                                dbComp.ModifiedBy = currentUser.EmployeeId;
                                dbContext.Entry(dbComp).Property(a => a.ModifiedOn).IsModified = true;
                                dbContext.Entry(dbComp).Property(a => a.ModifiedBy).IsModified = true;
                            }

                            dbComp.Value = comp.Value;
                            dbComp.Range = comp.Range;
                            dbComp.RangeDescription = comp.RangeDescription;
                            dbComp.Remarks = comp.Remarks;
                            dbComp.IsAbnormal = comp.IsAbnormal;
                            dbComp.AbnormalType = comp.AbnormalType;
                            dbComp.IsActive = true;
                            dbComp.IsNegativeResult = comp.IsNegativeResult;
                            dbComp.ResultGroup = comp.ResultGroup;





                            dbContext.Entry(dbComp).Property(a => a.Value).IsModified = true;
                            dbContext.Entry(dbComp).Property(a => a.IsNegativeResult).IsModified = true;
                            dbContext.Entry(dbComp).Property(a => a.Range).IsModified = true;
                            dbContext.Entry(dbComp).Property(a => a.RangeDescription).IsModified = true;
                            dbContext.Entry(dbComp).Property(a => a.IsAbnormal).IsModified = true;
                            dbContext.Entry(dbComp).Property(a => a.AbnormalType).IsModified = true;
                            dbContext.Entry(dbComp).Property(a => a.Remarks).IsModified = true;
                            dbContext.Entry(dbComp).Property(a => a.IsActive).IsModified = true;
                            dbContext.Entry(dbComp).Property(a => a.ResultGroup).IsModified = true;
                        }
                    }

                    //Section-5: Update IsActive Status of DeletedComponents.
                    if (deletedComps_Db.Count > 0)
                    {
                        foreach (var dbComp in deletedComps_Db)
                        {
                            dbComp.IsActive = false;
                            dbContext.Entry(dbComp).Property(a => a.IsActive).IsModified = true;
                        }
                    }

                }

                //YES: NOW After all Requisitions and Components are upated, we can call the SaveChanges Function()-- happy ?  :)
                dbContext.SaveChanges();

                //if (docPatPortalSync)
                //{
                //    DocPatPortalBL.PutLabReports(LabRequisition.LabReportId, distinctReqs, dbContext);
                //}

            }
        }


        private bool ValidatePrintOption(bool allowOutPatWithProv, string visittype, string billingstatus)
        {
            if (visittype.ToLower() == "inpatient")
            {
                return true;
            }
            else
            {
                if (allowOutPatWithProv)
                {
                    if (billingstatus.ToLower() == "paid" || billingstatus.ToLower() == "unpaid" || billingstatus.ToLower() == "provisional")
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    if (billingstatus.ToLower() == "paid" || billingstatus.ToLower() == "unpaid" || visittype.ToLower() == "emergency")
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
        }

        private List<LabPendingResultVM> GetAllHTMLLabPendingResults(LabDbContext labDbContext,
            Int64 BarcodeNumber = 0,
            int SampleNumber = 0, int SampleCode = 0, DateTime EnglishDateToday = default(DateTime),
            int PatientId = 0, DateTime? StartDate = null, DateTime? EndDate = null, List<int> categoryList = null, string labType = "", bool forWorkList = false, int defaultVendorId = 1)
        {
            bool filterByDate = true;
            bool filterByCategory = false;

            if (categoryList != null)
            {
                filterByCategory = true;
            }

            if (StartDate == null || EndDate == null)
            {
                filterByDate = false;
            }

            var htmlPendingResult = (from req in labDbContext.Requisitions
                                     join test in labDbContext.LabTests on req.LabTestId equals test.LabTestId
                                     join template in labDbContext.LabReportTemplates on req.ReportTemplateId equals template.ReportTemplateID
                                     join patient in labDbContext.Patients on req.PatientId equals patient.PatientId
                                     where (forWorkList ? ((req.OrderStatus.ToLower() == ENUM_LabOrderStatus.Pending) || (req.OrderStatus.ToLower() == ENUM_LabOrderStatus.ResultAdded)
                                        || (req.OrderStatus.ToLower() == ENUM_LabOrderStatus.ReportGenerated)) : (req.OrderStatus.ToLower() == ENUM_LabOrderStatus.Pending))
                                     && req.SampleCode != null
                                     && (filterByDate ? (DbFunctions.TruncateTime(req.CreatedOn) >= StartDate && DbFunctions.TruncateTime(req.CreatedOn) <= EndDate) : true)
                                     && req.BillingStatus.ToLower() != ENUM_BillingStatus.cancel // "cancel" 
                                     && req.BillingStatus.ToLower() != ENUM_BillingStatus.returned // "returned"
                                     && (BarcodeNumber == 0 ? true : (req.BarCodeNumber == BarcodeNumber))
                                     && (SampleNumber == 0 ? true : (req.SampleCode.HasValue ? (req.SampleCode == SampleNumber) : false))
                                     && (PatientId == 0 ? true : (req.PatientId == PatientId))
                                     //&& (req.BillingStatus == "paid" || (req.BillingStatus == "provisional" && req.VisitType == "inpatient"))
                                     && (template.TemplateType.ToLower() == ENUM_LabTemplateType.html) // "html")
                                     && (filterByCategory ? (categoryList.Contains(test.LabTestCategoryId.Value)) : true)
                                     && (req.LabTypeName == labType)
                                     && (req.ResultingVendorId == defaultVendorId)
                                     group new { req, test, template } by new
                                     {
                                         patient,
                                         req.SampleCode,
                                         req.SampleCodeFormatted,
                                         DbFunctions.TruncateTime(req.SampleCreatedOn).Value,
                                         req.VisitType,
                                         req.RequisitionId,
                                         req.RunNumberType,
                                         req.BarCodeNumber,
                                         req.WardName,
                                         req.HasInsurance
                                     } into grp
                                     select new LabPendingResultVM
                                     {
                                         PatientId = grp.Key.patient.PatientId,
                                         PatientCode = grp.Key.patient.PatientCode,
                                         DateOfBirth = grp.Key.patient.DateOfBirth,
                                         PhoneNumber = grp.Key.patient.PhoneNumber,
                                         Gender = grp.Key.patient.Gender,
                                         PatientName = grp.Key.patient.FirstName + " " + (string.IsNullOrEmpty(grp.Key.patient.MiddleName) ? "" : grp.Key.patient.MiddleName + " ") + grp.Key.patient.LastName,

                                         SampleCode = grp.Key.SampleCode,
                                         SampleDate = grp.Key.Value,
                                         VisitType = grp.Key.VisitType,
                                         RunNumType = grp.Key.RunNumberType,
                                         BarCodeNumber = grp.Key.BarCodeNumber,
                                         WardName = grp.Key.WardName,
                                         SampleCodeFormatted = grp.Key.SampleCodeFormatted,
                                         HasInsurance = grp.Key.HasInsurance,
                                         Tests = grp.Select(a =>
                                         new LabPendingResultVM.LabTestDetail()
                                         {
                                             RequisitionId = a.req.RequisitionId,
                                             TestName = a.test.LabTestName,
                                             LabTestId = a.test.LabTestId,
                                             ReportTemplateId = a.template.ReportTemplateID,
                                             ReportTemplateShortName = a.template.ReportTemplateShortName,
                                             RunNumberType = a.req.RunNumberType,
                                             BillingStatus = a.req.BillingStatus,
                                             IsVerified = a.req.IsVerified
                                         }).OrderBy(req => req.RequisitionId).ToList()
                                     }).ToList();

            return htmlPendingResult;
        }


        private List<LabPendingResultVM> GetAllNormalLabPendingResults(LabDbContext labDbContext,
            Int64 BarcodeNumber = 0,
            int SampleNumber = 0, int SampleCode = 0, DateTime EnglishDateToday = default(DateTime),
            int PatientId = 0, DateTime? StartDate = null, DateTime? EndDate = null, List<int> categoryList = null, string labType = "", bool forWorkList = false, int defaultVendorId = 1)
        {
            bool filterByDate = true;
            bool filterByCategory = false;

            if (categoryList != null)
            {
                filterByCategory = true;
            }

            if (StartDate == null || EndDate == null)
            {
                filterByDate = false;
            }

            var normalPendingResults = (from req in labDbContext.Requisitions
                                        join test in labDbContext.LabTests on req.LabTestId equals test.LabTestId
                                        join template in labDbContext.LabReportTemplates on req.ReportTemplateId equals template.ReportTemplateID
                                        join patient in labDbContext.Patients on req.PatientId equals patient.PatientId
                                        where (forWorkList ? ((req.OrderStatus.ToLower() == ENUM_LabOrderStatus.Pending) || (req.OrderStatus.ToLower() == ENUM_LabOrderStatus.ResultAdded)
                                        || (req.OrderStatus.ToLower() == ENUM_LabOrderStatus.ReportGenerated)) : (req.OrderStatus.ToLower() == ENUM_LabOrderStatus.Pending))
                                        && req.SampleCode != null
                                        && req.BillingStatus.ToLower() != ENUM_BillingStatus.cancel //"cancel"
                                        && req.BillingStatus.ToLower() != ENUM_BillingStatus.returned //"returned"
                                        && (BarcodeNumber == 0 ? true : (req.BarCodeNumber == BarcodeNumber))
                                        && (SampleNumber == 0 ? true : (req.SampleCode.HasValue ? (req.SampleCode == SampleNumber) : false))
                                        && (filterByDate ? (DbFunctions.TruncateTime(req.CreatedOn) >= StartDate && DbFunctions.TruncateTime(req.CreatedOn) <= EndDate) : true)
                                        && (PatientId == 0 ? true : (req.PatientId == PatientId))
                                        //Removed as all can add result but cannot Print Report Until Bill is Paid (incase of OP)
                                        //&& (req.BillingStatus == "paid" || (req.BillingStatus == "provisional" && req.VisitType == "inpatient"))
                                        //&& (template.TemplateType.ToLower() == "normal" || template.TemplateType.ToLower() == "culture")
                                          && (template.TemplateType.ToLower() == ENUM_LabTemplateType.normal || template.TemplateType.ToLower() == ENUM_LabTemplateType.culture)
                                        && (filterByCategory ? (categoryList.Contains(test.LabTestCategoryId.Value)) : true)
                                        && (req.LabTypeName == labType)
                                        && (req.ResultingVendorId == defaultVendorId)
                                        group new { req, test, template } by new
                                        {
                                            patient,
                                            req.SampleCode,
                                            req.SampleCodeFormatted,
                                            DbFunctions.TruncateTime(req.SampleCreatedOn).Value,
                                            req.VisitType,
                                            req.RunNumberType,
                                            req.BarCodeNumber,
                                            req.WardName,
                                            req.HasInsurance
                                        } into grp
                                        select new LabPendingResultVM
                                        {
                                            PatientId = grp.Key.patient.PatientId,
                                            PatientCode = grp.Key.patient.PatientCode,
                                            DateOfBirth = grp.Key.patient.DateOfBirth,
                                            PhoneNumber = grp.Key.patient.PhoneNumber,
                                            Gender = grp.Key.patient.Gender,
                                            PatientName = grp.Key.patient.FirstName + " " + (string.IsNullOrEmpty(grp.Key.patient.MiddleName) ? "" : grp.Key.patient.MiddleName + " ") + grp.Key.patient.LastName,

                                            SampleCode = grp.Key.SampleCode,
                                            SampleDate = grp.Key.Value,
                                            VisitType = grp.Key.VisitType,
                                            RunNumType = grp.Key.RunNumberType,
                                            BarCodeNumber = grp.Key.BarCodeNumber,
                                            SampleCodeFormatted = grp.Key.SampleCodeFormatted,
                                            WardName = grp.Key.WardName,
                                            HasInsurance = grp.Key.HasInsurance,
                                            Tests = grp.Select(a =>
                                            new LabPendingResultVM.LabTestDetail()
                                            {
                                                RequisitionId = a.req.RequisitionId,
                                                TestName = a.test.LabTestName,
                                                LabTestId = a.test.LabTestId,
                                                ReportTemplateId = a.template.ReportTemplateID,
                                                ReportTemplateShortName = a.template.ReportTemplateShortName,
                                                BillingStatus = a.req.BillingStatus,
                                                IsVerified = a.req.IsVerified
                                            }).OrderBy(req => req.RequisitionId).ToList()
                                        }).ToList();

            return normalPendingResults;

        }

        private List<LabPendingResultVM> GetAllHTMLLabPendingReports(LabDbContext labDbContext,
            Int64 BarcodeNumber = 0,
            int SampleNumber = 0, int SampleCode = 0, DateTime EnglishDateToday = default(DateTime),
            int PatientId = 0, DateTime? StartDate = null, DateTime? EndDate = null, List<int> categoryList = null, string labType = "")
        {

            var verificationParameter = (from param in labDbContext.AdminParameters
                                         where param.ParameterGroupName.ToLower() == "lab" && param.ParameterName == "LabReportVerificationNeededB4Print"
                                         select param.ParameterValue).FirstOrDefault();

            var verificationObj = DanpheJSONConvert.DeserializeObject<VerificationCoreCFGModel>(verificationParameter);

            bool verificationRequired = verificationObj.EnableVerificationStep;
            int verificationLevel = verificationObj.VerificationLevel.Value;

            bool filterByDate = true;
            bool filterByCategory = false;

            if (categoryList != null)
            {
                filterByCategory = true;
            }

            if (StartDate == null || EndDate == null)
            {
                filterByDate = false;
            }

            var htmlPendingReports = (from req in labDbContext.Requisitions
                                      join test in labDbContext.LabTests on req.LabTestId equals test.LabTestId
                                      join template in labDbContext.LabReportTemplates on req.ReportTemplateId equals template.ReportTemplateID
                                      join patient in labDbContext.Patients on req.PatientId equals patient.PatientId
                                      where req.OrderStatus.ToLower() == ENUM_LabOrderStatus.ResultAdded // "result-added"
                                      && (BarcodeNumber == 0 ? true : (req.BarCodeNumber == BarcodeNumber))
                                      && (SampleNumber == 0 ? true : (req.SampleCode.HasValue ? (req.SampleCode == SampleNumber) : false))
                                      && (filterByDate ? (DbFunctions.TruncateTime(req.CreatedOn) >= StartDate && DbFunctions.TruncateTime(req.CreatedOn) <= EndDate) : true)
                                      && (filterByCategory ? (categoryList.Contains(test.LabTestCategoryId.Value)) : true)
                                      && (PatientId == 0 ? true : (req.PatientId == PatientId))
                                      && req.BillingStatus.ToLower() != ENUM_BillingStatus.cancel //"cancel" 
                                      && (req.BillingStatus.ToLower() != ENUM_BillingStatus.returned)//"returned")
                                      && (template.TemplateType.ToLower() == ENUM_LabTemplateType.html)// "html")
                                      && (req.LabTypeName == labType)
                                      group new { req, template, patient, test } by new
                                      {
                                          patient,
                                          req,
                                          req.RunNumberType,
                                          req.SampleCodeFormatted,
                                          req.BarCodeNumber,
                                          req.WardName,
                                          req.HasInsurance,
                                          req.LabReportId
                                      } into grp
                                      select new LabPendingResultVM
                                      {
                                          PatientId = grp.Key.patient.PatientId,
                                          PatientCode = grp.Key.patient.PatientCode,
                                          DateOfBirth = grp.Key.patient.DateOfBirth,
                                          PhoneNumber = grp.Key.patient.PhoneNumber,
                                          Gender = grp.Key.patient.Gender,
                                          PatientName = grp.Key.patient.FirstName + " " + (string.IsNullOrEmpty(grp.Key.patient.MiddleName) ? "" : grp.Key.patient.MiddleName + " ") + grp.Key.patient.LastName,

                                          SampleCode = grp.Key.req.SampleCode,
                                          SampleDate = DbFunctions.TruncateTime(grp.Key.req.SampleCreatedOn).Value,
                                          SampleCodeFormatted = grp.Key.SampleCodeFormatted,
                                          VisitType = grp.Key.req.VisitType,
                                          RunNumType = grp.Key.RunNumberType,
                                          BarCodeNumber = grp.Key.BarCodeNumber,
                                          WardName = grp.Key.WardName,
                                          ReportId = grp.Key.LabReportId,
                                          HasInsurance = grp.Key.HasInsurance,
                                          ResultAddedOn = grp.Max(a => a.req.ResultAddedOn),
                                          Tests = (from g in grp
                                                   select new LabPendingResultVM.LabTestDetail
                                                   {
                                                       RequisitionId = g.req.RequisitionId,
                                                       LabTestId = g.req.LabTestId,
                                                       TestName = g.req.LabTestName,
                                                       BillingStatus = g.req.BillingStatus,
                                                       LabReportId = g.req.LabReportId
                                                   }).Distinct().ToList()
                                          //Tests = (from requisition in labDbContext.Requisitions
                                          //         join test in labDbContext.LabTests on requisition.LabTestId equals test.LabTestId
                                          //         where requisition.PatientId == grp.Key.patient.PatientId
                                          //          && requisition.RequisitionId == grp.Key.req.RequisitionId
                                          //          && requisition.BarCodeNumber == grp.Key.BarCodeNumber
                                          //          && requisition.WardName == grp.Key.WardName
                                          //          && requisition.RunNumberType == grp.Key.RunNumberType
                                          //          && requisition.HasInsurance == grp.Key.HasInsurance
                                          //          && requisition.BillingStatus.ToLower() != ENUM_BillingStatus.cancel //"cancel" 
                                          //          && requisition.BillingStatus.ToLower() != ENUM_BillingStatus.returned //"returned"
                                          //          && requisition.OrderStatus.ToLower() == ENUM_LabOrderStatus.ResultAdded// "result-added"
                                          //                                                                                 //group requisition by new { test } into g
                                          //         select new LabPendingResultVM.LabTestDetail
                                          //         {
                                          //             RequisitionId = requisition.RequisitionId,
                                          //             LabTestId = requisition.LabTestId,
                                          //             TestName = requisition.LabTestName,
                                          //             BillingStatus = requisition.BillingStatus,
                                          //             LabReportId = requisition.LabReportId
                                          //             //RequisitionId = g.Select(a => a.RequisitionId).FirstOrDefault(),
                                          //             //LabTestId = g.Key.test.LabTestId,
                                          //             //TestName = g.Key.test.LabTestName
                                          //         }).ToList()
                                      }).ToList();

            return htmlPendingReports;

        }

        private List<LabPendingResultVM> GetAllNormalLabPendingReports(LabDbContext labDbContext,
            Int64 BarcodeNumber = 0,
            int SampleNumber = 0, int SampleCode = 0, DateTime EnglishDateToday = default(DateTime),
            int PatientId = 0, DateTime? StartDate = null, DateTime? EndDate = null, List<int> categoryList = null, string labType = "")
        {
            var verificationParameter = (from param in labDbContext.AdminParameters
                                         where param.ParameterGroupName.ToLower() == "lab" && param.ParameterName == "LabReportVerificationNeededB4Print"
                                         select param.ParameterValue).FirstOrDefault();

            var verificationObj = DanpheJSONConvert.DeserializeObject<VerificationCoreCFGModel>(verificationParameter);

            bool verificationRequired = verificationObj.EnableVerificationStep;
            int verificationLevel = verificationObj.VerificationLevel.Value;

            bool filterByDate = true;
            bool filterByCategory = false;

            if (categoryList != null)
            {
                filterByCategory = true;
            }
            if (StartDate == null || EndDate == null)
            {
                filterByDate = false;
            }

            var normalPendingReports = (from req in labDbContext.Requisitions
                                        join test in labDbContext.LabTests on req.LabTestId equals test.LabTestId
                                        join template in labDbContext.LabReportTemplates on req.ReportTemplateId equals template.ReportTemplateID
                                        join patient in labDbContext.Patients on req.PatientId equals patient.PatientId
                                        where (verificationRequired ? (req.OrderStatus.ToLower() == ENUM_LabOrderStatus.ResultAdded //"result-added" 
                                        && (req.IsVerified.HasValue ? req.IsVerified == false : true)) : req.OrderStatus.ToLower() == ENUM_LabOrderStatus.ResultAdded) // "result-added"
                                        && (BarcodeNumber == 0 ? true : (req.BarCodeNumber == BarcodeNumber))
                                        && (SampleNumber == 0 ? true : (req.SampleCode.HasValue ? (req.SampleCode == SampleNumber) : false))
                                        && (filterByDate ? (DbFunctions.TruncateTime(req.CreatedOn) >= StartDate && DbFunctions.TruncateTime(req.CreatedOn) <= EndDate) : true)
                                        && (filterByCategory ? (categoryList.Contains(test.LabTestCategoryId.Value)) : true)
                                        && (PatientId == 0 ? true : (req.PatientId == PatientId))
                                        && req.BillingStatus.ToLower() != ENUM_BillingStatus.cancel //"cancel" 
                                        && req.BillingStatus.ToLower() != ENUM_BillingStatus.returned //"returned"
                                        //&& (template.TemplateType.ToLower() == "normal" || template.TemplateType.ToLower() == "culture")
                                        && (template.TemplateType.ToLower() == ENUM_LabTemplateType.normal || template.TemplateType.ToLower() == ENUM_LabTemplateType.culture)
                                        && (req.LabTypeName == labType)
                                        group new { req, template, patient, test } by new
                                        {
                                            patient,
                                            req.SampleCode,
                                            DbFunctions.TruncateTime(req.SampleCreatedOn).Value,
                                            req.VisitType,
                                            req.RunNumberType,
                                            req.BarCodeNumber,
                                            req.WardName,
                                            req.LabReportId,
                                            req.SampleCodeFormatted,
                                            req.HasInsurance
                                        } into grp
                                        select new LabPendingResultVM
                                        {
                                            PatientId = grp.Key.patient.PatientId,
                                            PatientCode = grp.Key.patient.PatientCode,
                                            DateOfBirth = grp.Key.patient.DateOfBirth,
                                            PhoneNumber = grp.Key.patient.PhoneNumber,
                                            Gender = grp.Key.patient.Gender,
                                            PatientName = grp.Key.patient.FirstName + " " + (string.IsNullOrEmpty(grp.Key.patient.MiddleName) ? "" : grp.Key.patient.MiddleName + " ") + grp.Key.patient.LastName,

                                            SampleCode = grp.Key.SampleCode,
                                            SampleDate = grp.Key.Value,
                                            VisitType = grp.Key.VisitType,
                                            RunNumType = grp.Key.RunNumberType,
                                            BarCodeNumber = grp.Key.BarCodeNumber,
                                            WardName = grp.Key.WardName,
                                            ReportId = grp.Key.LabReportId,
                                            SampleCodeFormatted = grp.Key.SampleCodeFormatted,
                                            HasInsurance = grp.Key.HasInsurance,
                                            ResultAddedOn = grp.Max(a => a.req.ResultAddedOn),
                                            Tests = (from g in grp
                                                     select new LabPendingResultVM.LabTestDetail
                                                     {
                                                         RequisitionId = g.req.RequisitionId,
                                                         LabTestId = g.req.LabTestId,
                                                         TestName = g.req.LabTestName,
                                                         BillingStatus = g.req.BillingStatus,
                                                         LabReportId = g.req.LabReportId,
                                                         CovidFileUrl = !string.IsNullOrEmpty(g.req.GoogleFileIdForCovid) ? CovidReportUrlComonPath.Replace("GGLFILEUPLOADID", g.req.GoogleFileIdForCovid) : ""
                                                     }).Distinct().ToList()
                                            //Tests = (from requisition in labDbContext.Requisitions
                                            //         join test in labDbContext.LabTests on requisition.LabTestId equals test.LabTestId
                                            //         join template in labDbContext.LabReportTemplates on requisition.ReportTemplateId equals template.ReportTemplateID
                                            //         where requisition.PatientId == grp.Key.patient.PatientId
                                            //        && requisition.SampleCode == grp.Key.SampleCode
                                            //        && requisition.VisitType == grp.Key.VisitType
                                            //        && requisition.WardName == grp.Key.WardName
                                            //        && requisition.BarCodeNumber == grp.Key.BarCodeNumber
                                            //        && requisition.RunNumberType == grp.Key.RunNumberType
                                            //        && requisition.LabReportId == grp.Key.LabReportId
                                            //        && requisition.HasInsurance == grp.Key.HasInsurance
                                            //        && DbFunctions.TruncateTime(requisition.SampleCreatedOn).Value == grp.Key.Value
                                            //        && requisition.OrderStatus.ToLower() == ENUM_LabOrderStatus.ResultAdded // "result-added"
                                            //        && requisition.BillingStatus.ToLower() != ENUM_BillingStatus.cancel //"cancel" 
                                            //        && requisition.BillingStatus.ToLower() != ENUM_BillingStatus.returned //"returned"
                                            //        && (template.TemplateType.ToLower() == ENUM_LabTemplateType.normal // "normal" 
                                            //        || template.TemplateType.ToLower() == ENUM_LabTemplateType.culture // "culture"
                                            //        )
                                            //         // group new { requisition }   by new { requisition, test } into g
                                            //         select new LabPendingResultVM.LabTestDetail
                                            //         {
                                            //             RequisitionId = requisition.RequisitionId,
                                            //             LabTestId = requisition.LabTestId,
                                            //             TestName = requisition.LabTestName,
                                            //             BillingStatus = requisition.BillingStatus,
                                            //             LabReportId = requisition.LabReportId
                                            //         }).Distinct().ToList()
                                        }).ToList();

            return normalPendingReports;
        }

        private List<SPFlatReportVM> GetAllLabFinalReportsFromSP(LabDbContext labDbContext,
            Int64 BarcodeNumber = 0, int SampleNumber = 0, int SampleCode = 0, DateTime EnglishDateToday = default(DateTime),
            int PatientId = 0, DateTime? StartDate = null, DateTime? EndDate = null, List<int> categoryList = null, string labType = "", bool isForLabMaster = false)
        {
            if (StartDate == null || EndDate == null)
            {
                StartDate = StartDate ?? System.DateTime.Now.AddYears(-20);
                EndDate = EndDate ?? System.DateTime.Now;
            }

            string categoryCsv = String.Join(",", categoryList.Select(x => x.ToString()).ToArray());

            List<SqlParameter> paramList = new List<SqlParameter>() {
                            new SqlParameter("@BarcodeNumber", BarcodeNumber),
                            new SqlParameter("@SampleNumber", SampleNumber),
                            new SqlParameter("@PatientId", PatientId),
                            new SqlParameter("@StartDate", StartDate),
                            new SqlParameter("@EndDate", EndDate),
                            new SqlParameter("@CategoryList", categoryCsv),
                            new SqlParameter("@Labtype", labType),
                            new SqlParameter("@IsForLabMaster", isForLabMaster),
                        };

            DataSet dataFromSP = DALFunctions.GetDatasetFromStoredProc("SP_LAB_GetAllLabProvisionalFinalReports", paramList, labDbContext);

            List<SPFlatReportVM> dSP = new List<SPFlatReportVM>();
            var data = new List<object>();
            if (dataFromSP.Tables.Count > 0)
            {
                var resultStr = JsonConvert.SerializeObject(dataFromSP.Tables[0]);
                dSP = DanpheJSONConvert.DeserializeObject<List<SPFlatReportVM>>(resultStr);
            }

            return dSP;
        }


        public IEnumerable<FinalLabReportListVM> GetFinalReportListFormatted(List<SPFlatReportVM> data)
        {
            var retData = (from repData in data
                           group repData by new
                           {
                               repData.SampleCodeFormatted,
                               repData.SampleCode,
                               repData.LabReportId,
                               repData.SampleDate,
                               repData.VisitType,
                               repData.RunNumType,
                               repData.IsPrinted,
                               repData.BarCodeNumber,
                               repData.WardName,
                               repData.HasInsurance,
                               repData.PatientId,
                               repData.PatientCode,
                               repData.PatientName,
                               repData.DateOfBirth,
                               repData.PhoneNumber,
                               repData.Gender,
                               repData.ReportGeneratedBy,
                               repData.ReportGeneratedById,
                               repData.AllowOutpatientWithProvisional,
                               repData.BillingStatus
                           } into grp
                           let isValidToPrintReport = (grp.Key.VisitType.ToLower() == "inpatient") ? true : (grp.Key.AllowOutpatientWithProvisional ? true : (grp.Key.VisitType.ToLower() == "emergency" ? true : false))
                           select new FinalLabReportListVM
                           {
                               PatientId = grp.Key.PatientId,
                               PatientCode = grp.Key.PatientCode,
                               DateOfBirth = grp.Key.DateOfBirth,
                               PhoneNumber = grp.Key.PhoneNumber,
                               Gender = grp.Key.Gender,
                               PatientName = grp.Key.PatientName,
                               SampleCode = grp.Key.SampleCode,
                               SampleDate = grp.Key.SampleDate,
                               SampleCodeFormatted = grp.Key.SampleCodeFormatted,
                               VisitType = grp.Key.VisitType,
                               RunNumType = grp.Key.RunNumType,
                               IsPrinted = grp.Key.IsPrinted,
                               BillingStatus = grp.Key.BillingStatus,
                               BarCodeNumber = grp.Key.BarCodeNumber,
                               ReportId = grp.Key.LabReportId,
                               WardName = grp.Key.WardName,
                               ReportGeneratedBy = grp.Key.ReportGeneratedBy,
                               LabTestCSV = string.Join(",", grp.Select(g => g.LabTestName).Distinct()),
                               LabRequisitionIdCSV = string.Join(",", grp.Select(g => g.RequisitionId).Distinct()),
                               AllowOutpatientWithProvisional = grp.Key.AllowOutpatientWithProvisional,
                               IsValidToPrint = (grp.Key.BillingStatus == "provisional") ? isValidToPrintReport : true,
                               Tests = (from g in grp
                                        select new FinalLabReportListVM.FinalReportListLabTestDetail
                                        {
                                            RequisitionId = g.RequisitionId,
                                            TestName = g.LabTestName,
                                            BillingStatus = g.BillingStatus,
                                            ValidTestToPrint = (g.BillingStatus == "provisional") ? isValidToPrintReport : true,
                                            SampleCollectedBy = g.SampleCollectedBy,
                                            PrintedBy = g.PrintedBy,
                                            ResultAddedBy = g.ResultAddedBy,
                                            VerifiedBy = g.VerifiedBy,
                                            PrintCount = g.PrintCount.HasValue ? g.PrintCount : 0
                                        }).ToList()
                           }).AsEnumerable();

            return retData;
        }


        public object GetFinalReportListFormattedInFinalReportPage(IEnumerable<FinalLabReportListVM> data)
        {
            var returnData = (from reportData in data
                              select new
                              {
                                  PatientId = reportData.PatientId,
                                  PatientCode = reportData.PatientCode,
                                  DateOfBirth = reportData.DateOfBirth,
                                  PhoneNumber = reportData.PhoneNumber,
                                  Gender = reportData.Gender,
                                  PatientName = reportData.PatientName,
                                  SampleCodeFormatted = reportData.SampleCodeFormatted,
                                  VisitType = reportData.VisitType,
                                  RunNumType = reportData.RunNumType,
                                  IsPrinted = reportData.IsPrinted,
                                  BillingStatus = reportData.BillingStatus,
                                  BarCodeNumber = reportData.BarCodeNumber,
                                  ReportId = reportData.ReportId,
                                  WardName = reportData.WardName,
                                  ReportGeneratedBy = reportData.ReportGeneratedBy,
                                  LabTestCSV = reportData.LabTestCSV,
                                  LabRequisitionIdCSV = reportData.LabRequisitionIdCSV,
                                  AllowOutpatientWithProvisional = reportData.AllowOutpatientWithProvisional,
                                  IsValidToPrint = reportData.IsValidToPrint
                              }).ToList();
            return returnData;
        }

        public object GetFinalReportListFormattedInFinalReportDispatchPage(IEnumerable<FinalLabReportListVM> data)
        {
            var start = System.DateTime.Now;
            var returnData = (from reportData in data
                              group reportData by new { reportData.PatientId, reportData.PatientCode, reportData.PatientName, reportData.PhoneNumber, reportData.Gender, reportData.WardName } into grp
                              select new
                              {
                                  PatientId = grp.Key.PatientId,
                                  PatientCode = grp.Key.PatientCode,
                                  PhoneNumber = grp.Key.PhoneNumber,
                                  Gender = grp.Key.Gender,
                                  PatientName = grp.Key.PatientName,
                                  WardName = grp.Key.WardName,
                                  IsSelected = false,
                                  //Reports = grp.Select(s => new
                                  //{
                                  //    SampleCodeFormatted = s.SampleCodeFormatted,
                                  //    IsSelected = false,
                                  //    Tests = s.Tests
                                  //}).ToList()
                              }).ToList();

            var diff = System.DateTime.Now.Subtract(start).TotalMilliseconds;
            return returnData;
        }

        private List<LabPendingResultVM> GetAllLabProvisionalFinalReports(LabDbContext labDbContext,
            Int64 BarcodeNumber = 0, int SampleNumber = 0, int SampleCode = 0, DateTime EnglishDateToday = default(DateTime),
            int PatientId = 0, DateTime? StartDate = null, DateTime? EndDate = null, List<int> categoryList = null, string labType = "", bool isForLabMaster = false)
        {
            bool filterByDate = true;
            bool filterByCategory = false;
            bool filterByLabType = !isForLabMaster;


            if (categoryList != null)
            {
                filterByCategory = true;
            }
            if (StartDate == null || EndDate == null)
            {
                filterByDate = false;
            }


            var parameterOutPatWithProvisional = (from coreData in labDbContext.AdminParameters
                                                  where coreData.ParameterGroupName.ToLower() == "lab"
                                                  && coreData.ParameterName == "AllowLabReportToPrintOnProvisional"
                                                  select coreData.ParameterValue).FirstOrDefault();

            bool allowOutPatWithProv = false;
            if (!String.IsNullOrEmpty(parameterOutPatWithProvisional) && parameterOutPatWithProvisional.ToLower() == "true")
            {
                allowOutPatWithProv = true;
            }

            var finalReportsProv = (from req in labDbContext.Requisitions
                                    join report in labDbContext.LabReports on req.LabReportId equals report.LabReportId
                                    join employee in labDbContext.Employee on report.CreatedBy equals employee.EmployeeId
                                    join test in labDbContext.LabTests on req.LabTestId equals test.LabTestId
                                    join patient in labDbContext.Patients on req.PatientId equals patient.PatientId
                                    where req.OrderStatus == ENUM_LabOrderStatus.ReportGenerated // "report-generated"
                                    && (BarcodeNumber == 0 ? true : (req.BarCodeNumber == BarcodeNumber))
                                    && (SampleNumber == 0 ? true : (req.SampleCode.HasValue ? (req.SampleCode == SampleNumber) : false))
                                    && (filterByCategory ? (categoryList.Contains(test.LabTestCategoryId.Value)) : true)
                                    && (PatientId == 0 ? true : (req.PatientId == PatientId))
                                    && req.BillingStatus == ENUM_BillingStatus.provisional // "provisional"
                                    && (filterByDate ? (report.CreatedOn.HasValue && DbFunctions.TruncateTime(report.CreatedOn.Value) >= StartDate && DbFunctions.TruncateTime(report.CreatedOn.Value) <= EndDate) : true)
                                    && (filterByLabType ? (req.LabTypeName == labType) : true)
                                    group new { req, patient, test } by new
                                    {
                                        patient,
                                        req.SampleCode,
                                        req.SampleCodeFormatted,
                                        req.LabReportId,
                                        employee.FullName,
                                        employee.EmployeeId,
                                        DbFunctions.TruncateTime(req.SampleCreatedOn).Value,
                                        req.VisitType,
                                        req.RunNumberType,
                                        report.IsPrinted,
                                        req.BarCodeNumber,
                                        req.WardName,
                                        req.HasInsurance
                                    } into grp
                                    let isValidToPrintReport = (grp.Key.VisitType.ToLower() == "inpatient") ? true : (allowOutPatWithProv ? true : (grp.Key.VisitType.ToLower() == "emergency" ? true : false))
                                    select new LabPendingResultVM
                                    {
                                        PatientId = grp.Key.patient.PatientId,
                                        PatientCode = grp.Key.patient.PatientCode,
                                        DateOfBirth = grp.Key.patient.DateOfBirth,
                                        PhoneNumber = grp.Key.patient.PhoneNumber,
                                        Gender = grp.Key.patient.Gender,
                                        PatientName = grp.Key.patient.FirstName + " " + (string.IsNullOrEmpty(grp.Key.patient.MiddleName) ? "" : grp.Key.patient.MiddleName + " ") + grp.Key.patient.LastName,

                                        SampleCode = grp.Key.SampleCode,
                                        SampleDate = grp.Key.Value,
                                        SampleCodeFormatted = grp.Key.SampleCodeFormatted,
                                        //SampleCodeFormatted = "",
                                        VisitType = grp.Key.VisitType,
                                        RunNumType = grp.Key.RunNumberType,
                                        IsPrinted = grp.Key.IsPrinted,
                                        BillingStatus = ENUM_BillingStatus.provisional, // "provisional",
                                        BarCodeNumber = grp.Key.BarCodeNumber,
                                        ReportId = grp.Key.LabReportId,
                                        WardName = grp.Key.WardName,
                                        HasInsurance = grp.Key.HasInsurance,
                                        ReportGeneratedBy = grp.Key.FullName,
                                        IsValidToPrint = isValidToPrintReport,
                                        Tests = (from g in grp
                                                 let isValidToPrintTest = (grp.Key.VisitType.ToLower() == "inpatient") ? true : (allowOutPatWithProv ? true : (grp.Key.VisitType.ToLower() == "emergency" ? true : false))
                                                 select new LabPendingResultVM.LabTestDetail
                                                 {
                                                     RequisitionId = g.req.RequisitionId,
                                                     LabTestId = g.req.LabTestId,
                                                     TestName = g.req.LabTestName,
                                                     SampleCollectedBy = g.req.SampleCreatedBy,
                                                     VerifiedBy = g.req.VerifiedBy,
                                                     ResultAddedBy = g.req.ResultAddedBy,
                                                     PrintCount = g.req.PrintCount == null ? 0 : g.req.PrintCount,
                                                     PrintedBy = g.req.PrintedBy,
                                                     BillingStatus = g.req.BillingStatus,
                                                     LabCategoryId = g.test.LabTestCategoryId,
                                                     ValidTestToPrint = isValidToPrintTest
                                                 }).ToList()
                                    }).OrderByDescending(d => d.SampleDate).ThenByDescending(x => x.SampleCode).ThenByDescending(a => a.PatientId).ToList();

            return finalReportsProv;
        }

        private List<LabPendingResultVM> GetAllLabPaidUnpaidFinalReports(LabDbContext labDbContext,
            Int64 BarcodeNumber = 0, int SampleNumber = 0, int SampleCode = 0, DateTime EnglishDateToday = default(DateTime),
            int PatientId = 0, DateTime? StartDate = null, DateTime? EndDate = null, List<int> categoryList = null, string labType = "",
            bool isForLabMaster = false)
        {
            bool filterByDate = true;
            bool filterByCategory = false;

            if (categoryList != null)
            {
                filterByCategory = true;
            }

            if (StartDate == null || EndDate == null)
            {
                StartDate = StartDate ?? System.DateTime.Now.AddYears(-10);
                EndDate = EndDate ?? System.DateTime.Now;
                filterByDate = false;
            }
            var finalReportsPaidUnpaid = (from req in labDbContext.Requisitions
                                          join report in labDbContext.LabReports on req.LabReportId equals report.LabReportId
                                          join employee in labDbContext.Employee on report.CreatedBy equals employee.EmployeeId
                                          join test in labDbContext.LabTests on req.LabTestId equals test.LabTestId
                                          join patient in labDbContext.Patients on req.PatientId equals patient.PatientId
                                          where req.OrderStatus == ENUM_LabOrderStatus.ReportGenerated //"report-generated"
                                          && (BarcodeNumber == 0 ? true : (req.BarCodeNumber == BarcodeNumber))
                                          && (SampleNumber == 0 ? true : (req.SampleCode.HasValue ? (req.SampleCode == SampleNumber) : false))
                                          && (PatientId == 0 ? true : (req.PatientId == PatientId))
                                          && (filterByCategory ? (categoryList.Contains(test.LabTestCategoryId.Value)) : true)
                                          && (req.BillingStatus.ToLower() == ENUM_BillingStatus.paid // "paid" 
                                          || req.BillingStatus.ToLower() == ENUM_BillingStatus.unpaid // "unpaid"
                                          )
                                          && (filterByDate ? (report.CreatedOn.HasValue && DbFunctions.TruncateTime(report.CreatedOn.Value) >= StartDate && DbFunctions.TruncateTime(report.CreatedOn.Value) <= EndDate) : true)
                                          && (!isForLabMaster ? (req.LabTypeName == labType) : true)
                                          group new { req, patient, test } by new
                                          {
                                              patient,
                                              req.SampleCode,
                                              req.SampleCodeFormatted,
                                              req.LabReportId,
                                              DbFunctions.TruncateTime(req.SampleCreatedOn).Value,
                                              req.VisitType,
                                              req.RunNumberType,
                                              report.IsPrinted,
                                              req.BarCodeNumber,
                                              req.WardName,
                                              req.HasInsurance,
                                              employee.FullName,
                                              employee.EmployeeId,
                                          } into grp

                                          select new LabPendingResultVM
                                          {
                                              PatientId = grp.Key.patient.PatientId,
                                              PatientCode = grp.Key.patient.PatientCode,
                                              DateOfBirth = grp.Key.patient.DateOfBirth,
                                              PhoneNumber = grp.Key.patient.PhoneNumber,
                                              Gender = grp.Key.patient.Gender,
                                              PatientName = grp.Key.patient.FirstName + " " + (string.IsNullOrEmpty(grp.Key.patient.MiddleName) ? "" : grp.Key.patient.MiddleName + " ") + grp.Key.patient.LastName,

                                              SampleCode = grp.Key.SampleCode,
                                              SampleDate = grp.Key.Value,
                                              SampleCodeFormatted = grp.Key.SampleCodeFormatted,
                                              //SampleCodeFormatted = "",
                                              VisitType = grp.Key.VisitType,
                                              RunNumType = grp.Key.RunNumberType,
                                              IsPrinted = grp.Key.IsPrinted,
                                              BillingStatus = "paid",
                                              BarCodeNumber = grp.Key.BarCodeNumber,
                                              ReportId = grp.Key.LabReportId,
                                              WardName = grp.Key.WardName,
                                              HasInsurance = grp.Key.HasInsurance,
                                              ReportGeneratedBy = grp.Key.FullName,
                                              IsValidToPrint = true,
                                              Tests = (from g in grp
                                                       select new LabPendingResultVM.LabTestDetail
                                                       {
                                                           RequisitionId = g.req.RequisitionId,
                                                           LabTestId = g.req.LabTestId,
                                                           TestName = g.req.LabTestName,
                                                           SampleCollectedBy = g.req.SampleCreatedBy,
                                                           VerifiedBy = g.req.VerifiedBy,
                                                           ResultAddedBy = g.req.ResultAddedBy,
                                                           PrintCount = g.req.PrintCount == null ? 0 : g.req.PrintCount,
                                                           PrintedBy = g.req.PrintedBy,
                                                           BillingStatus = g.req.BillingStatus,
                                                           LabCategoryId = g.test.LabTestCategoryId,
                                                           ValidTestToPrint = true
                                                       }).ToList()
                                          }).ToList();
            return finalReportsPaidUnpaid;
        }

        private LatestLabSampleCodeDetailVM GenerateLabSampleCode(LabDbContext labDbContext, string runNumType, string visitType, int patId, DateTime sampleDate, bool hasInsurance = false)
        {
            DataSet barcod = DALFunctions.GetDatasetFromStoredProc("SP_LAB_GetLatestBarCodeNumber", null, labDbContext);
            var strData = JsonConvert.SerializeObject(barcod.Tables[0]);
            List<BarCodeNumber> barCode = DanpheJSONConvert.DeserializeObject<List<BarCodeNumber>>(strData);
            var BarCodeNumber = barCode[0].Value;

            List<LabRunNumberSettingsModel> allLabRunNumberSettings = (List<LabRunNumberSettingsModel>)DanpheCache.GetMasterData(MasterDataEnum.LabRunNumberSettings);


            List<LabRequisitionModel> allReqOfCurrentType = new List<LabRequisitionModel>();

            //Get current RunNumber Settings
            LabRunNumberSettingsModel currentRunNumSetting = allLabRunNumberSettings.Where(st => st.RunNumberType.ToLower() == runNumType.ToLower()
            && st.VisitType.ToLower() == visitType.ToLower() && st.UnderInsurance == hasInsurance).FirstOrDefault();

            //Get all the Rows based upon this GroupingIndex
            List<LabRunNumberSettingsModel> allCommonSetting = allLabRunNumberSettings.Where(r =>
            r.RunNumberGroupingIndex == currentRunNumSetting.RunNumberGroupingIndex).ToList();

            List<SqlParameter> paramList = new List<SqlParameter>() {
                        new SqlParameter("@SampleDate", sampleDate),
                        new SqlParameter("@PatientId", patId),
                        new SqlParameter("@GroupingIndex", currentRunNumSetting.RunNumberGroupingIndex)};
            DataSet dts = DALFunctions.GetDatasetFromStoredProc("SP_LAB_AllRequisitionsBy_VisitAndRunType", paramList, labDbContext);

            var strlatestSampleModel = JsonConvert.SerializeObject(dts.Tables[0]);
            List<SPLatestSampleCode> latestSampleModel = DanpheJSONConvert.DeserializeObject<List<SPLatestSampleCode>>(strlatestSampleModel);
            int latestSample = latestSampleModel[0].LatestSampleCode;


            List<SPExistingSampleCodeDetail> ExistingBarCodeNumbers = new List<SPExistingSampleCodeDetail>();
            if (dts.Tables.Count > 1)
            {
                var strAllReqOfCurrentType = JsonConvert.SerializeObject(dts.Tables[1]);
                ExistingBarCodeNumbers = DanpheJSONConvert.DeserializeObject<List<SPExistingSampleCodeDetail>>(strAllReqOfCurrentType);
            }


            var sampleLetter = string.Empty;
            var labSampleCode = string.Empty;

            if (currentRunNumSetting != null)
            {
                sampleLetter = currentRunNumSetting.StartingLetter;

                if (String.IsNullOrWhiteSpace(sampleLetter))
                {
                    sampleLetter = string.Empty;
                }

                var beforeSeparator = currentRunNumSetting.FormatInitialPart;
                var separator = currentRunNumSetting.FormatSeparator;
                var afterSeparator = currentRunNumSetting.FormatLastPart;

                if (beforeSeparator == "num")
                {
                    if (afterSeparator.Contains("yy"))
                    {
                        var afterSeparatorLength = afterSeparator.Length;
                        NepaliDateType nepDate = DanpheDateConvertor.ConvertEngToNepDate(sampleDate);
                        labSampleCode = nepDate.Year.ToString().Substring(1, afterSeparatorLength);
                    }
                    else if (afterSeparator.Contains("dd"))
                    {
                        NepaliDateType nepDate = DanpheDateConvertor.ConvertEngToNepDate(sampleDate);
                        labSampleCode = nepDate.Day.ToString();
                    }
                    else if (afterSeparator.Contains("mm"))
                    {
                        NepaliDateType nepDate = DanpheDateConvertor.ConvertEngToNepDate(sampleDate);
                        labSampleCode = nepDate.Month.ToString();
                    }
                }
                else
                {
                    if (beforeSeparator.Contains("yy"))
                    {
                        var beforeSeparatorLength = beforeSeparator.Length;
                        NepaliDateType nepDate = DanpheDateConvertor.ConvertEngToNepDate(sampleDate);
                        labSampleCode = nepDate.Year.ToString().Substring(1, beforeSeparatorLength);
                    }
                    else if (beforeSeparator.Contains("dd"))
                    {
                        NepaliDateType nepDate = DanpheDateConvertor.ConvertEngToNepDate(sampleDate);
                        labSampleCode = nepDate.Day.ToString();
                    }
                    else if (beforeSeparator.Contains("mm"))
                    {
                        NepaliDateType nepDate = DanpheDateConvertor.ConvertEngToNepDate(sampleDate);
                        labSampleCode = nepDate.Month.ToString();
                    }
                }
            }
            else
            {
                throw new ArgumentException("Cannot Get Samplecode");
            }

            LatestLabSampleCodeDetailVM data = new LatestLabSampleCodeDetailVM();
            data.SampleCode = labSampleCode;
            data.SampleNumber = latestSample;
            data.BarCodeNumber = BarCodeNumber;
            data.SampleLetter = sampleLetter;
            data.ExistingBarCodeNumbersOfPatient = (ExistingBarCodeNumbers != null && ExistingBarCodeNumbers.Count > 0) ? ExistingBarCodeNumbers[0] : null;

            return data;
        }


        private UpdatedSampleCodeReturnData UpdateSampleCode(LabDbContext labDbContext, List<PatientLabSampleVM> labTests, RbacUser currentUser)
        {
            string RunNumberType = null;
            string visitType = null;
            DateTime? sampleCreatedOn = null;

            //sample code for All Tests in Current Requests will be same.
            int? sampleNum = null;
            Int64? existingBarCodeNum = null;
            Int64? LabBarCodeNum = null;
            string reqIdList = "";
            bool? hasInsurance = labTests[0].HasInsurance;
            using (TransactionScope trans = new TransactionScope())
            {
                try
                {
                    var requisitionid = labTests[0].RequisitionId;
                    var allReqList = labTests.Select(s => s.RequisitionId).ToList();

                    var allRequisitionsFromDb = labDbContext.Requisitions.Where(a => allReqList.Contains(a.RequisitionId))
                                                        .ToList();

                    LabRequisitionModel currRequisitionType = allRequisitionsFromDb.Where(a => a.RequisitionId == requisitionid)
                                                                  .FirstOrDefault<LabRequisitionModel>();

                    //var barCodeList = (from v in labDbContext.LabBarCode
                    //                   select v).ToList();

                    Int64 lastBarCodeNum = (from bar in labDbContext.LabBarCode
                                            select bar.BarCodeNumber).DefaultIfEmpty(0).Max();
                    //if barcode number is not found then start from 1million (10 lakhs)
                    Int64 newBarCodeNumber = lastBarCodeNum != 0 ? lastBarCodeNum + 1 : 1000000;


                    RunNumberType = currRequisitionType.RunNumberType.ToLower();
                    visitType = currRequisitionType.VisitType.ToLower();
                    sampleCreatedOn = labTests[0].SampleCreatedOn;
                    sampleNum = labTests[0].SampleCode;
                    int patientId = currRequisitionType.PatientId;


                    //Get the GroupingIndex From visitType and Run Number Type
                    var currentSetting = (from runNumSetting in LabRunNumberSettings
                                          where runNumSetting.VisitType.ToLower() == visitType.ToLower()
                                          && runNumSetting.RunNumberType.ToLower() == RunNumberType.ToLower()
                                          && runNumSetting.UnderInsurance == hasInsurance
                                          select runNumSetting
                                         ).FirstOrDefault();

                    //get the requisition with same Run number
                    List<SqlParameter> paramList = new List<SqlParameter>() {
                        new SqlParameter("@SampleDate", sampleCreatedOn),
                        new SqlParameter("@SampleCode", sampleNum),
                        new SqlParameter("@PatientId", patientId),
                        new SqlParameter("@GroupingIndex", currentSetting.RunNumberGroupingIndex)};
                    DataSet dts = DALFunctions.GetDatasetFromStoredProc("SP_LAB_GetPatientExistingRequisition_With_SameRunNumber", paramList, labDbContext);

                    List<LabRequisitionModel> esistingReqOfPat = new List<LabRequisitionModel>();

                    if (dts.Tables.Count > 0)
                    {
                        var strPatExistingReq = JsonConvert.SerializeObject(dts.Tables[0]);
                        esistingReqOfPat = DanpheJSONConvert.DeserializeObject<List<LabRequisitionModel>>(strPatExistingReq);
                        currRequisitionType = (esistingReqOfPat.Count > 0) ? esistingReqOfPat[0] : null;
                    }
                    else
                    {
                        currRequisitionType = null;
                    }

                    if (currRequisitionType != null)
                    {
                        existingBarCodeNum = currRequisitionType.BarCodeNumber;
                        LabBarCodeModel newBarCode = labDbContext.LabBarCode.Where(c => c.BarCodeNumber == existingBarCodeNum)
                                                            .FirstOrDefault<LabBarCodeModel>();

                        newBarCode.IsActive = true;
                        labDbContext.Entry(newBarCode).Property(a => a.IsActive).IsModified = true;
                        labDbContext.SaveChanges();

                        sampleCreatedOn = currRequisitionType.SampleCreatedOn;
                    }
                    else
                    {
                        if (existingBarCodeNum == null)
                        {
                            LabBarCodeModel barCode = new LabBarCodeModel();
                            barCode.BarCodeNumber = newBarCodeNumber;
                            barCode.IsActive = true;
                            barCode.CreatedBy = currentUser.EmployeeId;
                            barCode.CreatedOn = System.DateTime.Now;
                            labDbContext.LabBarCode.Add(barCode);
                            labDbContext.SaveChanges();
                        }
                    }

                    string formattedSampleCode = GetSampleCodeFormatted(sampleNum, labTests[0].SampleCreatedOn ?? default(DateTime), visitType, RunNumberType);
                    var sampleCollectedDateTime = System.DateTime.Now;
                    foreach (var test in labTests)
                    {
                        LabRequisitionModel dbRequisition = allRequisitionsFromDb.Where(r => r.RequisitionId == test.RequisitionId).FirstOrDefault();
                        reqIdList = reqIdList + test.RequisitionId + ",";
                        labDbContext.Requisitions.Attach(dbRequisition);
                        if (test.SampleCode != null)
                        {
                            dbRequisition.SampleCode = sampleNum = test.SampleCode;
                            dbRequisition.SampleCodeFormatted = formattedSampleCode;
                            dbRequisition.SampleCreatedOn = sampleCreatedOn;
                            dbRequisition.SampleCreatedBy = currentUser.EmployeeId;
                            dbRequisition.BarCodeNumber = existingBarCodeNum != null ? existingBarCodeNum : newBarCodeNumber;
                            dbRequisition.SampleCollectedOnDateTime = sampleCollectedDateTime;
                        }
                        dbRequisition.LabTestSpecimen = test.Specimen;
                        dbRequisition.OrderStatus = ENUM_LabOrderStatus.Pending;
                    }
                    LabBarCodeNum = existingBarCodeNum != null ? existingBarCodeNum : newBarCodeNumber;

                    labDbContext.SaveChanges();

                    reqIdList = reqIdList.Substring(0, (reqIdList.Length - 1));
                    List<SqlParameter> billingParamList = new List<SqlParameter>(){
                                                    new SqlParameter("@allReqIds", reqIdList),
                                                    new SqlParameter("@status", ENUM_BillingOrderStatus.Pending)
                                                };
                    DataTable statusUpdated = DALFunctions.GetDataTableFromStoredProc("SP_Bill_OrderStatusUpdate", billingParamList, labDbContext);
                    trans.Complete();

                    var data = new UpdatedSampleCodeReturnData();
                    data.FormattedSampleCode = formattedSampleCode;
                    data.BarCodeNumber = LabBarCodeNum;
                    data.SampleCollectedOnDateTime = sampleCollectedDateTime;
                    return data;
                }
                catch (Exception ex)
                {
                    throw (ex);
                }

            }
        }


        private LabRequisitionModel GetCurrentRequisitionData(LabDbContext labDbContext, int patId, string RunNumberType, string visitType,
            DateTime? sampleCreatedOn, int runNumber, bool? isInsurance)
        {
            LabRequisitionModel currRequisitionType = null;
            var isUnderInsurance = isInsurance.HasValue ? isInsurance.Value : false;




            //Get the GroupingIndex From visitType and Run Number Type
            var currentSetting = (from runNumSetting in LabRunNumberSettings
                                  where runNumSetting.VisitType.ToLower() == visitType.ToLower()
                                  && runNumSetting.RunNumberType.ToLower() == RunNumberType.ToLower()
                                  && runNumSetting.UnderInsurance == isUnderInsurance
                                  select runNumSetting
                                 ).FirstOrDefault();


            //Get all the Rows based upon this GroupingIndex
            var allRunNumSettingsByGroupingIndex = (from runNumSetting in LabRunNumberSettings
                                                    where runNumSetting.RunNumberGroupingIndex == currentSetting.RunNumberGroupingIndex
                                                    select new
                                                    {
                                                        runNumSetting.RunNumberType,
                                                        runNumSetting.VisitType,
                                                        runNumSetting.UnderInsurance,
                                                        runNumSetting.ResetDaily,
                                                        runNumSetting.ResetMonthly,
                                                        runNumSetting.ResetYearly
                                                    }).ToList();


            //Get all the Requisition of current sample date and sample code
            var reqOfCurrentSampleYear = (from req in labDbContext.Requisitions.Where(r => r.SampleCreatedOn.HasValue) //this already filters not null data.. 
                                          where req.SampleCode == runNumber && req.SampleCreatedOn.Value.Year == sampleCreatedOn.Value.Year
                                          select req).ToList();



            foreach (var currVal in allRunNumSettingsByGroupingIndex)
            {
                if (currentSetting.ResetYearly || currentSetting.ResetMonthly || currentSetting.ResetDaily)
                {

                    var repeatedSampleData = (from req in reqOfCurrentSampleYear
                                              where (currentSetting.ResetMonthly ? (DanpheDateConvertor.ConvertEngToNepDate(req.SampleCreatedOn.Value).Month == DanpheDateConvertor.ConvertEngToNepDate(sampleCreatedOn.Value).Month) : true)
                                              && (currentSetting.ResetDaily ? ((req.SampleCreatedOn.Value.Month == sampleCreatedOn.Value.Month)
                                              && (req.SampleCreatedOn.Value.Day == sampleCreatedOn.Value.Day)) : true)
                                              && req.VisitType.ToLower() == currVal.VisitType.ToLower()
                                              && req.RunNumberType.ToLower() == currVal.RunNumberType.ToLower()
                                              && req.HasInsurance == currVal.UnderInsurance
                                              && req.PatientId == patId
                                              select req).FirstOrDefault();

                    if (repeatedSampleData != null)
                    {
                        currRequisitionType = repeatedSampleData;
                    }
                }
                else
                {
                    throw new ArgumentException("Please set the reset type.");
                }

            }
            return currRequisitionType;
        }



        bool PostSms(LabDbContext dbctx, long selectedId, int userId)
        {
            try
            {

                var patientData = GetSmsMessageAndNumberOfPatientByReqId(dbctx, selectedId);
                if (patientData != null)
                {
                    var payLoad = patientData.Message;

                    var smsParamList = dbctx.AdminParameters.Where(p => (p.ParameterGroupName.ToLower() == "lab") && ((p.ParameterName == "SmsParameter") || (p.ParameterName == "LabSmsProviderName"))).Select(d => new { d.ParameterValue, d.ParameterName }).ToList();
                    var providerName = smsParamList.Where(s => s.ParameterName == "LabSmsProviderName").Select(d => d.ParameterValue).FirstOrDefault() ?? "Sparrow";
                    var smsParam = smsParamList.Where(s => s.ParameterName == "SmsParameter").Select(d => d.ParameterValue).FirstOrDefault() ?? "[]";
                    var smsParamObj = JsonConvert.DeserializeObject<List<dynamic>>(smsParam);

                    var selectedProviderDetail = smsParamObj.Where(p => p["SmsProvider"] == providerName).FirstOrDefault();
                    if (selectedProviderDetail != null)
                    {
                        string key = selectedProviderDetail["Token"];

                        if (providerName == "LumbiniTech")
                        {
                            string url = selectedProviderDetail["Url"];
                            url = url.Replace("SMSKEY", key);
                            url = url.Replace("SMSPHONENUMBER", patientData.PhoneNumber);
                            url = url.Replace("SMSMESSAGE", payLoad);

                            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                            request.Method = "GET";
                            request.ContentType = "application/json";
                            HttpWebResponse response = (HttpWebResponse)request.GetResponse();

                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                LabSMSModel data = new LabSMSModel();
                                data.RequisitionId = Convert.ToInt32(selectedId);
                                data.Message = payLoad;
                                data.CreatedOn = System.DateTime.Now;
                                data.CreatedBy = userId;

                                dbctx.LabSms.Add(data);
                                dbctx.SaveChanges();

                                var reqStr = selectedId.ToString();
                                List<SqlParameter> paramList = new List<SqlParameter>() { new SqlParameter("@RequistionIds", reqStr) };
                                DataSet dts = DALFunctions.GetDatasetFromStoredProc("SP_LAB_Update_Test_SmsStatus", paramList, dbctx);
                                dbctx.SaveChanges();

                                return true;
                            }
                            else
                            {
                                return false;
                            }
                            //lumbinitech implementation
                        }
                        else if (providerName == "Sparrow")
                        {
                            //sparrow implementation
                            return false;
                        }
                        else
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }

            }
            catch (Exception ex)
            {
                throw ex;
            }
        }


    }



}



