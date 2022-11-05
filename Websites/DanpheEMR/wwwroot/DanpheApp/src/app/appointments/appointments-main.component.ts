import { Component } from '@angular/core'
import { RouterOutlet, RouterModule, Router } from '@angular/router'
import { SecurityService } from "../security/shared/security.service"
import { DanpheCache, MasterType } from '../shared/danphe-cache-service-utility/cache-services';
import { VisitBLService } from './shared/visit.bl.service';
import { DanpheHTTPResponse } from '../shared/common-models';
import { VisitService } from './shared/visit.service';
import { CoreService } from '../core/shared/core.service';
import { CallbackService } from '../shared/callback.service';
import { BillingBLService } from '../billing/shared/billing.bl.service';
import { BillingService } from '../billing/shared/billing.service';

@Component({
  selector: 'my-app',
  templateUrl: "./appointments-main.html"
})

// App Component class
export class AppointmentsMainComponent {
  validRoutes: any;
  public primaryNavItems: Array<any> = null;
  public secondaryNavItems: Array<any> = null;
  public currentCounter: number = null;

  constructor(public securityService: SecurityService,
    public visitBLService: VisitBLService,
    public visitService: VisitService, public coreService: CoreService,
    public callbackService: CallbackService,public router: Router,
    public billingBlService: BillingBLService,
    public billingService: BillingService,) {

    DanpheCache.GetData(MasterType.AllMasters, null);//sud:25June'19--Dunno what this is doing here.. 
    // get the chld routes of Appointment from valid routes available for this user.
    this.validRoutes = this.securityService.GetChildRoutes("Appointment");
    this.primaryNavItems = this.validRoutes.filter(a => a.IsSecondaryNavInDropdown == null || a.IsSecondaryNavInDropdown == 0);
    this.secondaryNavItems = this.validRoutes.filter(a => a.IsSecondaryNavInDropdown == 1);
    //sud: this will load all necessary masters into visit service's variables
    this.LoadDoctorAndDeptPricesToVisitService();
    this.LoadAllBillingItems();
    this.GetOrganizationList();

    this.currentCounter = this.securityService.getLoggedInCounter().CounterId;
    if(this.currentCounter <1){
      this.callbackService.CallbackRoute = '/Appointment/PatientSearch';
      this.router.navigate(['/Billing/CounterActivate']);
    }

  }

   async LoadDoctorAndDeptPricesToVisitService() {

    if (this.visitService.DocFollowupPrices && this.visitService.DocFollowupPrices.length == 0) {
      this.visitBLService.GetDoctorFollowupItems()
        .subscribe((res: DanpheHTTPResponse) => {
          if (res.Status == "OK") {
            this.visitService.DocFollowupPrices = res.Results;
          }
        });
    }

    this.visitBLService.GetDepartmentFollowupItems()
      .subscribe((res: DanpheHTTPResponse) => {
        if (res.Status == "OK") {
          this.visitService.DeptFollowupPrices = res.Results;
        }
      });

    this.visitBLService.GetDoctorOpdPrices()
      .subscribe((res: DanpheHTTPResponse) => {
        if (res.Status == "OK") {
          this.visitService.DocOpdPrices = res.Results;
        }
      });

    this.visitBLService.GetDepartmentOpdItems()
      .subscribe((res: DanpheHTTPResponse) => {
        if (res.Status == "OK") {
          this.visitService.DeptOpdPrices = res.Results;
        }
      });

    //sud: 31Jul'19-For Old Patient Opd
    this.visitBLService.GetDepartmentOldPatientPrices()
      .subscribe((res: DanpheHTTPResponse) => {
        if (res.Status == "OK") {
          this.visitService.DeptOpdPrice_OldPatient = res.Results;
        }
      });
    //sud: 31Jul'19-For Old Patient Opd
    this.visitBLService.GetDoctorOldPatientPrices()
      .subscribe((res: DanpheHTTPResponse) => {
        if (res.Status == "OK") {
          this.visitService.DocOpdPrice_OldPatient = res.Results;
        }
      });

     this.visitBLService.GetDepartment()
       .subscribe((res: DanpheHTTPResponse) => {
         if (res.Status == "OK") {
           this.visitService.ApptApplicableDepartmentList = res.Results;
           console.log("Department list loaded successfully");
          //  this.visitService.ApptApplicableDepartmentList =  this.coreService.Masters.Departments.filter(d => d.IsAppointmentApplicable == true && d.IsActive == true).map(d => {
          //    return {
          //      DepartmentId: d.DepartmentId,
          //      DepartmentName: d.DepartmentName
          //    };
          //  });
         }

       });

     //sud:6Sept'21: removed below code to take department from cache.. it's not working.. hence can't use below. 
    //DepartmentData is already available, so re-use it..
    //  this.visitService.ApptApplicableDepartmentList = await this.coreService.Masters.Departments.filter(d => d.IsAppointmentApplicable == true && d.IsActive == true).map(d => {
    //   return {
    //     DepartmentId: d.DepartmentId,
    //     DepartmentName: d.DepartmentName
    //   };
    // });

    //check if we can get employee data also from pre-loaded masters.
    this.visitBLService.GetVisitDoctors()
      .subscribe((res: DanpheHTTPResponse) => {
        if (res.Status == "OK") {
          this.visitService.ApptApplicableDoctorsList = res.Results;
        }
      });
  }

  //we have to load all billing items into service variable, which will be used across this module. 
  public LoadAllBillingItems() {
    this.visitBLService.GetBillItemList()
      .subscribe((res: DanpheHTTPResponse) => {
        if (res.Status == "OK") {
          console.log("bill item prices are loaded successfully (appointment-main).");
          this.visitService.LoadAllBillItemsPriceList(res.Results);
        }
        else {
          console.log("Couldn't load bill item prices. (appointment-main)");
        }
      });
  }

  public GetOrganizationList() {
    this.billingBlService.GetOrganizationList()
      .subscribe((res: DanpheHTTPResponse) => {
        if (res.Status == 'OK') {
          console.log("CreditOrganization list are loaded successfully (billing-main).");
          this.billingService.SetAllCreditOrgList(res.Results);
        }
        else {
          console.log("Couldn't get CreditOrganization List(billing-main).");
        }
      });
  }

}





